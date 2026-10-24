using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("Arture/UI/World Space Message Icon")]
public sealed class WorldSpaceMessageIcon : MaskableGraphic
{
    private WorldSpaceMessageType messageType = WorldSpaceMessageType.Info;
    private string direction = "right";
    private const float Stroke = 4.5f;

    public void SetMessageType(WorldSpaceMessageType type, string arrowDirection)
    {
        messageType = type;
        direction = (arrowDirection ?? "right").Trim().ToLowerInvariant();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        float halfWidth = rectTransform.rect.width * 0.5f;
        float halfHeight = rectTransform.rect.height * 0.5f;
        float unit = Mathf.Min(halfWidth, halfHeight);

        switch (messageType)
        {
            case WorldSpaceMessageType.PointArrow:
                DrawPointArrow(vertexHelper, unit);
                break;
            case WorldSpaceMessageType.TurnArrow:
                DrawTurnArrow(vertexHelper, unit);
                break;
            case WorldSpaceMessageType.Alert:
                DrawCircle(vertexHelper, Vector2.zero, unit * 0.77f, Stroke, false);
                DrawLine(vertexHelper, new Vector2(0f, unit * 0.35f), new Vector2(0f, -unit * 0.08f), Stroke);
                DrawCircle(vertexHelper, new Vector2(0f, -unit * 0.42f), unit * 0.055f, 0f, true);
                break;
            case WorldSpaceMessageType.Warning:
                DrawPolygon(vertexHelper, new[]
                {
                    new Vector2(0f, unit * 0.84f),
                    new Vector2(unit * 0.83f, -unit * 0.68f),
                    new Vector2(-unit * 0.83f, -unit * 0.68f)
                }, Stroke);
                DrawLine(vertexHelper, new Vector2(0f, unit * 0.30f), new Vector2(0f, -unit * 0.16f), Stroke);
                DrawCircle(vertexHelper, new Vector2(0f, -unit * 0.45f), unit * 0.055f, 0f, true);
                break;
            default:
                DrawCircle(vertexHelper, Vector2.zero, unit * 0.78f, Stroke, false);
                DrawLine(vertexHelper, new Vector2(0f, unit * 0.12f), new Vector2(0f, -unit * 0.34f), Stroke);
                DrawCircle(vertexHelper, new Vector2(0f, unit * 0.46f), unit * 0.055f, 0f, true);
                break;
        }
    }

    private void DrawPointArrow(VertexHelper vh, float u)
    {
        Vector2 start = new Vector2(-u * 0.62f, -u * 0.56f);
        Vector2 tip = DirectionVector() * (u * 0.63f);
        DrawLine(vh, start, tip, Stroke);
        Vector2 directionVector = (tip - start).normalized;
        Vector2 side = new Vector2(-directionVector.y, directionVector.x);
        DrawLine(vh, tip, tip - directionVector * u * 0.30f + side * u * 0.20f, Stroke);
        DrawLine(vh, tip, tip - directionVector * u * 0.30f - side * u * 0.20f, Stroke);
        DrawCircle(vh, new Vector2(-u * 0.47f, -u * 0.50f), u * 0.12f, Stroke * 0.8f, false);
    }

    private void DrawTurnArrow(VertexHelper vh, float u)
    {
        bool left = direction == "left";
        bool down = direction == "down";
        var points = new List<Vector2>();
        if (down)
        {
            float sign = left ? -1f : 1f;
            points.Add(new Vector2(-u * 0.12f, u * 0.62f));
            points.Add(new Vector2(-u * 0.12f, u * 0.10f));
            points.Add(new Vector2(sign * u * 0.12f, -u * 0.10f));
            points.Add(new Vector2(sign * u * 0.55f, -u * 0.10f));
            DrawPolyline(vh, points, Stroke);
            Vector2 tip = points[points.Count - 1];
            float directionSign = left ? -1f : 1f;
            DrawLine(vh, tip, tip - new Vector2(directionSign * u * 0.26f, u * 0.18f), Stroke);
            DrawLine(vh, tip, tip - new Vector2(directionSign * u * 0.26f, -u * 0.18f), Stroke);
            return;
        }

        float mirror = left ? -1f : 1f;
        points.Add(new Vector2(-mirror * u * 0.55f, -u * 0.55f));
        points.Add(new Vector2(-mirror * u * 0.55f, -u * 0.12f));
        points.Add(new Vector2(-mirror * u * 0.38f, u * 0.14f));
        points.Add(new Vector2(mirror * u * 0.28f, u * 0.14f));
        points.Add(new Vector2(mirror * u * 0.53f, u * 0.39f));
        DrawPolyline(vh, points, Stroke);
        Vector2 arrowTip = points[points.Count - 1];
        DrawLine(vh, arrowTip, arrowTip - new Vector2(mirror * u * 0.30f, 0f), Stroke);
        DrawLine(vh, arrowTip, arrowTip - new Vector2(0f, u * 0.30f), Stroke);
    }

    private Vector2 DirectionVector()
    {
        switch (direction)
        {
            case "left": return Vector2.left;
            case "down": return Vector2.down;
            case "forward": return Vector2.up;
            default: return Vector2.right;
        }
    }

    private void DrawPolyline(VertexHelper vh, IList<Vector2> points, float width)
    {
        for (int i = 1; i < points.Count; i++)
            DrawLine(vh, points[i - 1], points[i], width);
    }

    private void DrawLine(VertexHelper vh, Vector2 start, Vector2 end, float width)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude < 0.0001f) return;
        Vector2 offset = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
        AddQuad(vh, start + offset, start - offset, end - offset, end + offset);
    }

    private void DrawCircle(VertexHelper vh, Vector2 center, float radius, float width, bool filled)
    {
        const int segments = 24;
        float innerRadius = filled ? 0f : Mathf.Max(0f, radius - width);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector2 outer0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
            Vector2 outer1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
            Vector2 inner0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * innerRadius;
            Vector2 inner1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * innerRadius;
            AddQuad(vh, outer0, inner0, inner1, outer1);
        }
    }

    private void DrawPolygon(VertexHelper vh, IReadOnlyList<Vector2> points, float width)
    {
        for (int i = 0; i < points.Count; i++)
            DrawLine(vh, points[i], points[(i + 1) % points.Count], width);
    }

    private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int start = vh.currentVertCount;
        AddVertex(vh, a);
        AddVertex(vh, b);
        AddVertex(vh, c);
        AddVertex(vh, d);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private void AddVertex(VertexHelper vh, Vector2 position)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = position;
        vh.AddVert(vertex);
    }
}
