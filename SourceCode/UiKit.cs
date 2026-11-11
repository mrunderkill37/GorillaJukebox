using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace GorillaJukebox;

/// <summary>
/// A rounded "plate": a coloured face with a slightly bigger, darker rim behind it.
/// The panel, the title bars, the equaliser screen and every button are plates.
/// </summary>
public class Plate
{
    public GameObject Go;
    public Renderer Rim;
    public Renderer Face;

    /// <summary>Sets the face colour. The rim becomes a darker shade of it.</summary>
    public void SetColor(Color face)
    {
        SetColors(face, UiKit.Darken(face, 0.55f));
    }

    public void SetColors(Color face, Color rim)
    {
        UiKit.SetColor(Face.sharedMaterial, face);
        UiKit.SetColor(Rim.sharedMaterial, rim);
    }
}

/// <summary>A pressable button: a rounded plate plus a text label. Pressed by touching it with your fingertip.</summary>
public class MenuButton
{
    public GameObject Go;    // the plate's root: slides toward you on hover and pushes back when pressed
    public Plate Plate;
    public TextMeshPro Label;
    public Vector2 Center;   // position inside the menu, in meters
    public Vector2 Half;     // half width / half height
    public Action OnPress;
    public bool Inside;      // was the fingertip touching last frame (so a press fires once per touch)
    public float FlashUntil;
    public Color ShownColor;
    public bool KeepLook;    // true = leave its colour and position alone (used by the song progress bar)
    public float Depth;      // how far the button currently sticks out toward you, in meters

    public void SetText(string text)
    {
        if (Label != null && Label.text != text) Label.text = text;
    }

    public void SetVisible(bool visible)
    {
        Go.SetActive(visible); // the label is a child, so it follows
    }
}

/// <summary>One page of the menu (main, song list, settings...).</summary>
public class MenuScreen
{
    public string Name;
    public GameObject Root;
    public List<MenuButton> Buttons = new List<MenuButton>();
    public Action OnShow;
}

/// <summary>Helpers that build the menu out of generated rounded shapes and 3D text, so no asset bundle is needed.</summary>
public static class UiKit
{
    private const int CornerSegments = 6; // how smooth the rounded corners are

    private static Shader shader;
    private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

    private static Shader FindShader()
    {
        if (shader != null) return shader;

        string[] candidates =
        {
            "Universal Render Pipeline/Unlit",
            "GorillaTag/UberShader",
            "Unlit/Color",
            "Sprites/Default",
            "Standard"
        };

        foreach (string name in candidates)
        {
            Shader s = Shader.Find(name);
            if (s != null)
            {
                shader = s;
                return shader;
            }
        }

        Debug.LogError($"[{Constants.Name}] No usable shader found for the menu.");
        return null;
    }

    public static void SetColor(Material material, Color color)
    {
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
    }

    public static Color Darken(Color color, float factor)
    {
        return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
    }

    public static Material NewMaterial(Color color)
    {
        Material material = new Material(FindShader());
        SetColor(material, color);
        return material;
    }

    /// <summary>
    /// A flat rounded rectangle centred on its object, facing the player (-Z). It is built double-sided,
    /// so it shows whichever way the shader culls.
    /// </summary>
    public static Renderer Shape(Transform parent, string name, Vector3 localPosition, Vector2 size, float radius, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        go.AddComponent<MeshFilter>().sharedMesh = RoundedRect(size.x, size.y, radius);

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    /// <summary>
    /// A rounded face with a darker rim behind it. "rim" is how far the rim sticks out on every side,
    /// "rimDrop" shifts it down a little so it reads as a shadow edge.
    /// </summary>
    public static Plate MakePlate(Transform parent, string name, Vector3 localPosition, Vector2 size, float radius,
        float rim, float rimDrop, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        Plate plate = new Plate { Go = go };
        plate.Rim = Shape(go.transform, "Rim", new Vector3(0f, -rimDrop, 0.0012f),
            new Vector2(size.x + rim * 2f, size.y + rim * 2f), radius + rim, NewMaterial(Darken(color, 0.55f)));
        plate.Face = Shape(go.transform, "Face", Vector3.zero, size, radius, NewMaterial(color));
        return plate;
    }

    /// <summary>A small sphere with no collider, used to show where your fingertip presses.</summary>
    public static GameObject Dot(Transform parent, string name, float diameter, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;

        // Purely visual: no collider, so it can't push the player or block anything.
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.Destroy(collider);

        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(diameter, diameter, diameter);

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = NewMaterial(color);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    // Builds (and caches) a rounded rectangle mesh in the XY plane, centred on the origin.
    private static Mesh RoundedRect(float width, float height, float radius)
    {
        radius = Mathf.Clamp(radius, 0.0001f, Mathf.Min(width, height) / 2f);

        string key = width.ToString("F5") + "|" + height.ToString("F5") + "|" + radius.ToString("F5");
        if (meshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

        float hw = width / 2f;
        float hh = height / 2f;
        Vector2[] centers =
        {
            new Vector2( hw - radius,  hh - radius), // top right
            new Vector2(-hw + radius,  hh - radius), // top left
            new Vector2(-hw + radius, -hh + radius), // bottom left
            new Vector2( hw - radius, -hh + radius), // bottom right
        };

        // Outline points, counter-clockwise, one rounded corner after another.
        List<Vector2> outline = new List<Vector2>();
        for (int c = 0; c < 4; c++)
        {
            for (int s = 0; s <= CornerSegments; s++)
            {
                float angle = (c * 90f + 90f * s / CornerSegments) * Mathf.Deg2Rad;
                outline.Add(centers[c] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        // Two copies of the shape: one facing -Z (the player), one facing +Z.
        int n = outline.Count;
        int back = n + 1;
        Vector3[] vertices = new Vector3[back * 2];
        Vector3[] normals = new Vector3[back * 2];
        Vector2[] uvs = new Vector2[back * 2];
        int[] triangles = new int[n * 6];

        for (int side = 0; side < 2; side++)
        {
            int offset = side * back;
            Vector3 normal = side == 0 ? Vector3.back : Vector3.forward;

            vertices[offset] = Vector3.zero;
            normals[offset] = normal;
            uvs[offset] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < n; i++)
            {
                Vector2 p = outline[i];
                vertices[offset + 1 + i] = new Vector3(p.x, p.y, 0f);
                normals[offset + 1 + i] = normal;
                uvs[offset + 1 + i] = new Vector2(p.x / width + 0.5f, p.y / height + 0.5f);
            }
        }

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;

            // Front (seen from -Z): clockwise, which is what Unity treats as the visible side.
            triangles[i * 6 + 0] = 0;
            triangles[i * 6 + 1] = 1 + next;
            triangles[i * 6 + 2] = 1 + i;

            // Back (seen from +Z): the same triangle the other way round.
            triangles[i * 6 + 3] = back;
            triangles[i * 6 + 4] = back + 1 + i;
            triangles[i * 6 + 5] = back + 1 + next;
        }

        Mesh mesh = new Mesh();
        mesh.name = "RoundedRect";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;

        meshes[key] = mesh;
        return mesh;
    }

    // fontSize is in "TextMeshPro 3D" units: roughly fontSize * 0.1 meters per em.
    public static TextMeshPro Text(Transform parent, string name, string text, Vector3 localPosition, Vector2 rect,
        float fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.richText = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.rectTransform.sizeDelta = rect;
        return tmp;
    }

    public static string Trim(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text.Substring(0, max - 1) + "...";
    }
}
