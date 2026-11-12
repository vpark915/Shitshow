using UnityEngine;
using UnityEngine.UIElements;

namespace JamzeySt.Carve.Editor
{
    public enum CarveGlyph { Dashboard, Materials, Convert, Cube, Gauge, Help, Graph, Plus, Collider, Chevron, Sun, Pyramid, Mesh, Search, Check, Caret }

    // Vector icons drawn on a 16-unit grid; colour comes from the USS custom property --icon-color (so hover/selected states recolour them).
    public class CarveIcon : VisualElement
    {
        static readonly CustomStyleProperty<Color> s_Color = new CustomStyleProperty<Color>("--icon-color");
        CarveGlyph m_Glyph;
        bool m_Flip;
        Color m_Color = new Color(0.6f, 0.63f, 0.67f);

        public CarveGlyph Glyph { get => m_Glyph; set { m_Glyph = value; MarkDirtyRepaint(); } }
        public bool Flip { get => m_Flip; set { m_Flip = value; MarkDirtyRepaint(); } }

        public CarveIcon(CarveGlyph glyph)
        {
            m_Glyph = glyph;
            AddToClassList("carve-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(s_Color, out var c) && c != m_Color) { m_Color = c; MarkDirtyRepaint(); }
            });
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s < 2f) return;
            var o = new Vector2(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f);
            float k = s / 16f;
            Vector2 P(float x, float y) => o + new Vector2(m_Flip ? 16f - x : x, y) * k;
            var p = ctx.painter2D;
            p.strokeColor = m_Color;
            p.fillColor = m_Color;
            p.lineWidth = Mathf.Max(1.2f, 1.45f * k);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            void Line(params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.Stroke();
            }
            void Poly(bool fill, params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }
            void Circle(float x, float y, float radius, bool fill)
            {
                p.BeginPath();
                p.Arc(P(x, y), radius * k, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }
            void Box(float x0, float y0, float x1, float y1, bool fill)
            {
                float rad = 1.2f * k;
                p.BeginPath();
                p.MoveTo(P(x0 + 1.2f, y0));
                p.ArcTo(P(x1, y0), P(x1, y1), rad);
                p.ArcTo(P(x1, y1), P(x0, y1), rad);
                p.ArcTo(P(x0, y1), P(x0, y0), rad);
                p.ArcTo(P(x0, y0), P(x1, y0), rad);
                p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }

            switch (m_Glyph)
            {
                case CarveGlyph.Dashboard:
                    Box(2f, 2f, 7f, 7f, true); Box(9f, 2f, 14f, 7f, true); Box(2f, 9f, 7f, 14f, true); Box(9f, 9f, 14f, 14f, true);
                    break;
                case CarveGlyph.Materials:
                    Circle(8f, 8f, 5.6f, false);
                    Circle(6.2f, 6.2f, 1.7f, true);
                    break;
                case CarveGlyph.Convert:
                    Line(P(2.5f, 5f), P(13f, 5f)); Line(P(10f, 2f), P(13f, 5f), P(10f, 8f));
                    Line(P(13.5f, 11f), P(3f, 11f)); Line(P(6f, 8f), P(3f, 11f), P(6f, 14f));
                    break;
                case CarveGlyph.Cube:
                    Poly(false, P(8f, 1.5f), P(14f, 4.8f), P(14f, 11.2f), P(8f, 14.5f), P(2f, 11.2f), P(2f, 4.8f));
                    Line(P(2f, 4.8f), P(8f, 8.2f), P(14f, 4.8f)); Line(P(8f, 8.2f), P(8f, 14.5f));
                    break;
                case CarveGlyph.Gauge:
                    p.BeginPath();
                    p.Arc(P(8f, 10.5f), 6f * k, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.Stroke();
                    Line(P(8f, 10.5f), P(11.2f, 6.2f));
                    Circle(8f, 10.5f, 1.4f, true);
                    break;
                case CarveGlyph.Help:
                    Circle(8f, 8f, 6.2f, false);
                    Circle(8f, 4.9f, 1f, true);
                    Line(P(8f, 7.4f), P(8f, 11.6f));
                    break;
                case CarveGlyph.Graph:
                    Box(1.5f, 2.5f, 6.5f, 7.5f, false); Box(9.5f, 8.5f, 14.5f, 13.5f, false);
                    p.BeginPath(); p.MoveTo(P(6.5f, 5f)); p.BezierCurveTo(P(10.5f, 5f), P(5.5f, 11f), P(9.5f, 11f)); p.Stroke();
                    break;
                case CarveGlyph.Plus:
                    Line(P(8f, 3f), P(8f, 13f)); Line(P(3f, 8f), P(13f, 8f));
                    break;
                case CarveGlyph.Collider:
                    p.BeginPath(); p.MoveTo(P(1f, 12.5f)); p.BezierCurveTo(P(4f, 8.5f), P(5.5f, 15f), P(8f, 12f)); p.BezierCurveTo(P(10.5f, 9f), P(12f, 14f), P(15f, 10.5f)); p.Stroke();
                    Circle(7.6f, 6.6f, 2.6f, false);
                    break;
                case CarveGlyph.Chevron:
                    Line(P(8.5f, 3.5f), P(4f, 8f), P(8.5f, 12.5f)); Line(P(13f, 3.5f), P(8.5f, 8f), P(13f, 12.5f));
                    break;
                case CarveGlyph.Sun:
                    Circle(8f, 8f, 3f, false);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Line(P(8f + d.x * 5.2f, 8f + d.y * 5.2f), P(8f + d.x * 6.8f, 8f + d.y * 6.8f));
                    }
                    break;
                case CarveGlyph.Pyramid:
                    Poly(false, P(8f, 2f), P(14.5f, 14f), P(1.5f, 14f));
                    Line(P(5f, 8f), P(11f, 8f)); Line(P(3.4f, 11f), P(12.6f, 11f));
                    break;
                case CarveGlyph.Mesh:
                    Poly(false, P(2f, 2f), P(14f, 2f), P(14f, 14f), P(2f, 14f));
                    Line(P(2f, 14f), P(14f, 2f)); Line(P(2f, 8f), P(8f, 2f)); Line(P(8f, 14f), P(14f, 8f));
                    break;
                case CarveGlyph.Search:
                    Circle(6.8f, 6.8f, 4.3f, false);
                    Line(P(10f, 10f), P(14f, 14f));
                    break;
                case CarveGlyph.Caret:
                    Line(P(5.5f, 2.5f), P(11f, 8f), P(5.5f, 13.5f));
                    break;
                case CarveGlyph.Check:
                    Line(P(3f, 8.5f), P(6.5f, 12f), P(13f, 4.5f));
                    break;
            }
        }
    }

    // The Carve mark: a faceted gem with relief strata cut into it. Colours from --logo-color and --logo-cut.
    public class CarveLogo : VisualElement
    {
        static readonly CustomStyleProperty<Color> s_Color = new CustomStyleProperty<Color>("--logo-color");
        static readonly CustomStyleProperty<Color> s_Cut = new CustomStyleProperty<Color>("--logo-cut");
        Color m_Color = new Color(0.91f, 0.63f, 0.31f), m_Cut = new Color(0.08f, 0.09f, 0.11f);

        public CarveLogo()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                e.customStyle.TryGetValue(s_Color, out m_Color);
                e.customStyle.TryGetValue(s_Cut, out m_Cut);
                MarkDirtyRepaint();
            });
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s < 4f) return;
            var o = new Vector2(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
            var p = ctx.painter2D;
            p.fillColor = m_Color;
            p.BeginPath();
            p.MoveTo(P(0.5f, 0.02f)); p.LineTo(P(0.98f, 0.5f)); p.LineTo(P(0.5f, 0.98f)); p.LineTo(P(0.02f, 0.5f));
            p.ClosePath();
            p.Fill();
            // Carved strata: three nested chevrons cut into the lower facets.
            p.strokeColor = m_Cut;
            p.lineJoin = LineJoin.Miter;
            p.lineCap = LineCap.Butt;
            p.lineWidth = s * 0.075f;
            for (int i = 0; i < 3; i++)
            {
                float y = 0.42f + i * 0.15f, w = 0.36f - i * 0.11f;
                p.BeginPath();
                p.MoveTo(P(0.5f - w, y - w * 0.55f));
                p.LineTo(P(0.5f, y));
                p.LineTo(P(0.5f + w, y - w * 0.55f));
                p.Stroke();
            }
        }
    }
}
