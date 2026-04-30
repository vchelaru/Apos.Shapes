using System;
using System.Text;
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace Apos.Shapes {
    public class ShapeBatch {
        public ShapeBatch(GraphicsDevice graphicsDevice, ContentManager content, Effect? effect = null) {
            _graphicsDevice = graphicsDevice;

            if (effect == null) {
                _effect = content.Load<Effect>("apos-shapes");
            } else {
                _effect = effect;
            }

            _vertices = new VertexShape[_initialVertices];
            _indices = new uint[_initialIndices];

            GenerateIndexArray();

            _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

            _indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits, _indices.Length, BufferUsage.WriteOnly);
            _indexBuffer.SetData(_indices);

            _fsr = new FontStashRenderer(graphicsDevice, this);
        }

        public GraphicsDevice GraphicsDevice => _graphicsDevice;

        public void Begin(Matrix? view = null, Matrix? projection = null, BlendState? blendState = null, SamplerState? samplerState = null, DepthStencilState? depthStencilState = null, RasterizerState? rasterizerState = null) {
            if (view != null) {
                _view = view.Value;
            } else {
                _view = Matrix.Identity;
            }

            if (projection != null) {
                _projection = projection.Value;
            } else {
                Viewport viewport = _graphicsDevice.Viewport;
                _projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
            }

            _pixelSize = ScreenToWorldScale();

            _blendState = blendState ?? BlendState.AlphaBlend;
            _samplerState = samplerState ?? SamplerState.LinearClamp;
            _depthStencilState = depthStencilState ?? DepthStencilState.None;
            _rasterizerState = rasterizerState ?? RasterizerState.CullCounterClockwise;
        }
        public void DrawCircle(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            if (dash != null && thickness > 0f) {
                DrawDashedCircle(center, radius, fill, border, thickness, rotation, aaSize, dash.Value);
                return;
            }
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            float aaOffset = _pixelSize * aaSize;
            float radius1 = radius + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius1);
            var topRight = center + new Vector2(radius1, -radius1);
            var bottomRight = center + new Vector2(radius1);
            var bottomLeft = center + new Vector2(-radius1, radius1);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius1, -radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, aaSize: aaSize);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius1, -radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, aaSize: aaSize);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius1, radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, aaSize: aaSize);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius1, radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, aaSize: aaSize);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawCircle(Vector2 center, float radius, Gradient fill, Color border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawCircle(center, radius, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rotation, aaSize, dash);
        }
        public void DrawCircle(Vector2 center, float radius, Color fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawCircle(center, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, rotation, aaSize, dash);
        }
        public void DrawCircle(Vector2 center, float radius, Color fill, Color border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawCircle(center, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero,  border, Gradient.Shape.None), thickness, rotation, aaSize, dash);
        }
        public void FillCircle(Vector2 center, float radius, Color c, float rotation = 0f, float aaSize = 1.5f) {
            DrawCircle(center, radius, c, c, 0f, rotation, aaSize);
        }
        public void FillCircle(Vector2 center, float radius, Gradient g, float rotation = 0f, float aaSize = 1.5f) {
            DrawCircle(center, radius, g, g, 0f, rotation, aaSize);
        }
        public void BorderCircle(Vector2 center, float radius, Color c, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawCircle(center, radius, Color.Transparent, c, thickness, rotation, aaSize, dash);
        }
        public void BorderCircle(Vector2 center, float radius, Gradient g, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawCircle(center, radius, new Gradient(Vector2.Zero, Color.Transparent, Vector2.Zero, Color.Transparent, Gradient.Shape.None), g, thickness, rotation, aaSize, dash);
        }
        private void DrawDashedCircle(Vector2 center, float radius, Gradient fill, Gradient border, float thickness, float rotation, float aaSize, DashPattern dash) {
            if (radius <= 0f || dash.Period <= 0f || dash.DashLength <= 0f) return;

            float innerRadius = MathF.Max(radius - thickness, 0f);
            if (innerRadius > 0f) {
                DrawCircle(center, innerRadius, fill, fill, 0f, rotation, aaSize, dash: null);
            }

            GradientToWorld(ref border, center, Vector2.Zero, rotation);
            border.IsLocal = false;

            float halfT = thickness * 0.5f;
            float ringRadius = radius - halfT;
            float circumference = 2f * MathF.PI * radius;

            float dashLen = dash.DashLength;
            float gapLen = dash.GapLength;
            float period = dash.Period;
            if (dash.FitToPath && circumference > 0f) {
                int n = Math.Max(1, (int)MathF.Round(circumference / period));
                float scale = circumference / (n * period);
                dashLen *= scale;
                gapLen *= scale;
                period *= scale;
            }

            float t = -dash.PhaseOffset;
            while (t >= period) t -= period;

            while (t < circumference) {
                float dashStart = MathF.Max(t, 0f);
                float dashEnd = MathF.Min(t + dashLen, circumference);
                if (dashEnd > dashStart) {
                    float a1 = rotation + dashStart / radius;
                    float a2 = rotation + dashEnd / radius;
                    // +1f cancels the radius1 -= 1f adjustment inside DrawRing so the
                    // dash ring lines up with where a solid BorderCircle's outline sits.
                    DrawRing(center, a1, a2, ringRadius + 1f, thickness, border, border, 0f, aaSize);
                }
                t += period;
            }
        }

        public void DrawRectangle(Vector2 xy, Vector2 size, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            if (dash != null && thickness > 0f) {
                DrawDashedRectangle(xy, size, fill, border, thickness, rounded, rotation, aaSize, dash.Value);
                return;
            }
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            rounded = MathF.Min(MathF.Min(rounded, size.X / 2f), size.Y / 2f);

            float aaOffset = _pixelSize * aaSize;
            Vector2 xy1 = xy - new Vector2(aaOffset); // Account for AA.
            Vector2 size1 = size + new Vector2(aaOffset * 2f); // Account for AA.
            Vector2 half = size / 2f;
            Vector2 half1 = half + new Vector2(aaOffset); // Account for AA.

            half -= new Vector2(rounded);

            var topLeft = xy1;
            var topRight = xy1 + new Vector2(size1.X, 0);
            var bottomRight = xy1 + size1;
            var bottomLeft = xy1 + new Vector2(0, size1.Y);

            Vector2 center = xy1 + half1;
            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, xy + half, -half, rotation);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-half1.X, -half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, half.Y, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(half1.X, -half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, half.Y, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(half1.X, half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, half.Y, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-half1.X, half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, half.Y, aaSize: aaSize, rounded: rounded);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawRectangle(Vector2 xy, Vector2 size, Gradient fill, Color border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawRectangle(xy, size, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, rotation, aaSize, dash);
        }
        public void DrawRectangle(Vector2 xy, Vector2 size, Color fill, Gradient border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawRectangle(xy, size, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, rounded, rotation, aaSize, dash);
        }
        public void DrawRectangle(Vector2 xy, Vector2 size, Color fill, Color border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawRectangle(xy, size, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, rotation, aaSize, dash);
        }
        public void FillRectangle(Vector2 xy, Vector2 size, Gradient g, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawRectangle(xy, size, g, g, 0f, rounded, rotation, aaSize);
        }
        public void FillRectangle(Vector2 xy, Vector2 size, Color c, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawRectangle(xy, size, c, c, 0f, rounded, rotation, aaSize);
        }
        public void BorderRectangle(Vector2 xy, Vector2 size, Gradient g, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawRectangle(xy, size, Color.Transparent, g, thickness, rounded, rotation, aaSize, dash);
        }
        public void BorderRectangle(Vector2 xy, Vector2 size, Color c, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawRectangle(xy, size, Color.Transparent, c, thickness, rounded, rotation, aaSize, dash);
        }
        private void DrawDashedRectangle(Vector2 xy, Vector2 size, Gradient fill, Gradient border, float thickness, float rounded, float rotation, float aaSize, DashPattern dash) {
            if (dash.Period <= 0f || dash.DashLength <= 0f) return;
            if (size.X <= 0f || size.Y <= 0f) return;

            rounded = MathF.Min(MathF.Min(rounded, size.X * 0.5f), size.Y * 0.5f);
            float halfT = thickness * 0.5f;

            Vector2 rectCenter = xy + size * 0.5f;
            Vector2 half = size * 0.5f;

            if (fill.IsLocal) GradientToWorld(ref fill, rectCenter, -half, rotation);
            fill.IsLocal = false;
            if (border.IsLocal) GradientToWorld(ref border, rectCenter, -half, rotation);
            border.IsLocal = false;

            Vector2 innerXY = xy + new Vector2(thickness);
            Vector2 innerSize = size - new Vector2(thickness * 2f);
            if (innerSize.X > 0f && innerSize.Y > 0f) {
                float innerRounded = MathF.Max(rounded - thickness, 0f);
                DrawRectangle(innerXY, innerSize, fill, fill, 0f, innerRounded, rotation, aaSize, dash: null);
            }

            float straightX = size.X - 2f * rounded;
            float straightY = size.Y - 2f * rounded;
            float cornerArc = rounded * MathF.PI * 0.5f;
            float perimeter = 2f * (straightX + straightY) + 4f * cornerArc;
            if (perimeter <= 0f) return;

            float ringRadius = MathF.Max(rounded - halfT, 0f);

            float topY = xy.Y;
            float bottomY = xy.Y + size.Y;
            float leftX = xy.X;
            float rightX = xy.X + size.X;

            Vector2 cornerTL = new(leftX + rounded, topY + rounded);
            Vector2 cornerTR = new(rightX - rounded, topY + rounded);
            Vector2 cornerBR = new(rightX - rounded, bottomY - rounded);
            Vector2 cornerBL = new(leftX + rounded, bottomY - rounded);

            float dashLen = dash.DashLength;
            float gapLen = dash.GapLength;
            float period = dash.Period;
            if (dash.FitToPath && perimeter > 0f) {
                int n = Math.Max(1, (int)MathF.Round(perimeter / period));
                float scale = perimeter / (n * period);
                dashLen *= scale;
                gapLen *= scale;
                period *= scale;
            }

            float t = -dash.PhaseOffset;
            t = ((t % period) + period) % period;
            t -= period;

            while (t < perimeter) {
                float dashStart = MathF.Max(t, 0f);
                float dashEnd = MathF.Min(t + dashLen, perimeter);
                if (dashEnd > dashStart) {
                    float segOff = 0f;

                    EmitStripDash(dashStart, dashEnd, segOff, straightX,
                        new Vector2(leftX + rounded, topY), new Vector2(1f, 0f), new Vector2(0f, 1f),
                        thickness, border, rectCenter, rotation, aaSize);
                    segOff += straightX;

                    if (rounded > 0f) {
                        EmitArcDash(dashStart, dashEnd, segOff, cornerArc,
                            cornerTR, -MathF.PI * 0.5f, 0f, ringRadius, thickness,
                            border, rectCenter, rotation, aaSize);
                    }
                    segOff += cornerArc;

                    EmitStripDash(dashStart, dashEnd, segOff, straightY,
                        new Vector2(rightX, topY + rounded), new Vector2(0f, 1f), new Vector2(-1f, 0f),
                        thickness, border, rectCenter, rotation, aaSize);
                    segOff += straightY;

                    if (rounded > 0f) {
                        EmitArcDash(dashStart, dashEnd, segOff, cornerArc,
                            cornerBR, 0f, MathF.PI * 0.5f, ringRadius, thickness,
                            border, rectCenter, rotation, aaSize);
                    }
                    segOff += cornerArc;

                    EmitStripDash(dashStart, dashEnd, segOff, straightX,
                        new Vector2(rightX - rounded, bottomY), new Vector2(-1f, 0f), new Vector2(0f, -1f),
                        thickness, border, rectCenter, rotation, aaSize);
                    segOff += straightX;

                    if (rounded > 0f) {
                        EmitArcDash(dashStart, dashEnd, segOff, cornerArc,
                            cornerBL, MathF.PI * 0.5f, MathF.PI, ringRadius, thickness,
                            border, rectCenter, rotation, aaSize);
                    }
                    segOff += cornerArc;

                    EmitStripDash(dashStart, dashEnd, segOff, straightY,
                        new Vector2(leftX, bottomY - rounded), new Vector2(0f, -1f), new Vector2(1f, 0f),
                        thickness, border, rectCenter, rotation, aaSize);
                    segOff += straightY;

                    if (rounded > 0f) {
                        EmitArcDash(dashStart, dashEnd, segOff, cornerArc,
                            cornerTL, MathF.PI, 1.5f * MathF.PI, ringRadius, thickness,
                            border, rectCenter, rotation, aaSize);
                    }
                }
                t += period;
            }
        }
        private void EmitStripDash(float dashStart, float dashEnd, float segStart, float segLen,
            Vector2 edgeStartLocal, Vector2 edgeDir, Vector2 inwardPerp,
            float thickness, Gradient border, Vector2 pivot, float rotation, float aaSize) {
            if (segLen <= 0f) return;
            float overlapStart = MathF.Max(dashStart, segStart);
            float overlapEnd = MathF.Min(dashEnd, segStart + segLen);
            if (overlapEnd <= overlapStart) return;

            float localStart = overlapStart - segStart;
            float localEnd = overlapEnd - segStart;
            float pieceLen = localEnd - localStart;

            Vector2 p1Local = edgeStartLocal + edgeDir * localStart;
            Vector2 p2Local = edgeStartLocal + edgeDir * localEnd;
            Vector2 pieceCenterLocal = (p1Local + p2Local) * 0.5f + inwardPerp * (thickness * 0.5f);
            Vector2 pieceCenterWorld = rotation != 0f ? Rotate(pieceCenterLocal, pivot, rotation) : pieceCenterLocal;

            float edgeAngle = MathF.Atan2(edgeDir.Y, edgeDir.X);
            Vector2 pieceSize = new(pieceLen, thickness);
            Vector2 pieceXY = pieceCenterWorld - pieceSize * 0.5f;

            DrawRectangle(pieceXY, pieceSize, border, border, 0f, 0f, edgeAngle + rotation, aaSize, dash: null);
        }
        private void EmitArcDash(float dashStart, float dashEnd, float segStart, float segLen,
            Vector2 arcCenterLocal, float arcStartAngle, float arcEndAngle, float ringRadius, float thickness,
            Gradient border, Vector2 pivot, float rotation, float aaSize) {
            if (segLen <= 0f) return;
            float overlapStart = MathF.Max(dashStart, segStart);
            float overlapEnd = MathF.Min(dashEnd, segStart + segLen);
            if (overlapEnd <= overlapStart) return;

            float t1 = (overlapStart - segStart) / segLen;
            float t2 = (overlapEnd - segStart) / segLen;

            // Take the short way around (matches DrawRing's behavior of drawing the
            // shorter arc), so the linear interpolation always traces the corner.
            float angleDelta = arcEndAngle - arcStartAngle;
            while (angleDelta > MathF.PI) angleDelta -= 2f * MathF.PI;
            while (angleDelta < -MathF.PI) angleDelta += 2f * MathF.PI;

            float a1 = arcStartAngle + t1 * angleDelta;
            float a2 = arcStartAngle + t2 * angleDelta;

            Vector2 arcCenterWorld = rotation != 0f ? Rotate(arcCenterLocal, pivot, rotation) : arcCenterLocal;
            // +1f cancels the radius1 -= 1f adjustment inside DrawRing so the corner ring
            // lines up with the straight-edge dashes (which go through DrawRectangle).
            DrawRing(arcCenterWorld, a1 + rotation, a2 + rotation, ringRadius + 1f, thickness, border, border, 0f, aaSize);
        }

        public void DrawLine(Vector2 a, Vector2 b, float radius, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f, DashPattern? dash = null) {
            if (dash != null && thickness > 0f) {
                DrawDashedLine(a, b, border, thickness, aaSize, dash.Value);
                return;
            }
            if (a == b) {
                DrawCircle(a, radius, fill, border, thickness, aaSize);
                return;
            }

            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            float aaOffset = _pixelSize * aaSize;
            var radius1 = radius + aaOffset; // Account for AA.

            var c = Slide(b, a, radius1);
            var d = Slide(a, b, radius1);

            var topLeft = Clockwise(c, d, radius1);
            var topRight = CounterClockwise(d, c, radius1);
            var bottomRight = Clockwise(d, c, radius1);
            var bottomLeft = CounterClockwise(c, d, radius1);

            var width = Vector2.Distance(a, b);
            var width1 = width + radius1; // Account for AA.

            GradientToWorld(ref fill, ref border, a, Vector2.Zero, MathF.Atan2(b.Y - a.Y, b.X - a.X));

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius1, -radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, width, aaSize: aaSize, rounded: radius);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(width1, -radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, width, aaSize: aaSize, rounded: radius);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(width1, radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, width, aaSize: aaSize, rounded: radius);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius1, radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, width, aaSize: aaSize, rounded: radius);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawLine(Vector2 a, Vector2 b, float radius, Gradient fill, Color border, float thickness = 1f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawLine(a, b, radius, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, aaSize, dash);
        }
        public void DrawLine(Vector2 a, Vector2 b, float radius, Color fill, Gradient border, float thickness = 1f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawLine(a, b, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, aaSize, dash);
        }
        public void DrawLine(Vector2 a, Vector2 b, float radius, Color fill, Color border, float thickness = 1f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawLine(a, b, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, aaSize, dash);
        }
        public void FillLine(Vector2 a, Vector2 b, float radius, Gradient g, float aaSize = 1.5f) {
            DrawLine(a, b, radius, g, g, 0f, aaSize);
        }
        public void FillLine(Vector2 a, Vector2 b, float radius, Color c, float aaSize = 1.5f) {
            DrawLine(a, b, radius, c, c, 0f, aaSize);
        }
        public void BorderLine(Vector2 a, Vector2 b, float radius, Gradient g, float thickness = 1f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawLine(a, b, radius, Color.Transparent, g, thickness, aaSize, dash);
        }
        public void BorderLine(Vector2 a, Vector2 b, float radius, Color c, float thickness = 1f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawLine(a, b, radius, Color.Transparent, c, thickness, aaSize, dash);
        }
        private void DrawDashedLine(Vector2 a, Vector2 b, Gradient border, float thickness, float aaSize, DashPattern dash) {
            float total = Vector2.Distance(a, b);
            if (total <= 0f || dash.Period <= 0f || dash.DashLength <= 0f) return;

            float rotation = MathF.Atan2(b.Y - a.Y, b.X - a.X);
            if (border.IsLocal) GradientToWorld(ref border, a, Vector2.Zero, rotation);
            border.IsLocal = false;

            Vector2 dir = (b - a) / total;
            float pieceHeight = thickness;

            // FitToPath on a line: M dashes, M-1 gaps, scaled so the pattern
            // starts and ends on a full dash.
            float dashLen = dash.DashLength;
            float gapLen = dash.GapLength;
            float period = dash.Period;
            if (dash.FitToPath) {
                int m = Math.Max(1, (int)MathF.Round((total + gapLen) / period));
                float patternLen = m * dashLen + (m - 1) * gapLen;
                if (patternLen > 0f) {
                    float scale = total / patternLen;
                    dashLen *= scale;
                    gapLen *= scale;
                    period *= scale;
                }
            }

            float t = -dash.PhaseOffset;
            while (t >= period) t -= period;

            while (t < total) {
                float dashStart = MathF.Max(t, 0f);
                float dashEnd = MathF.Min(t + dashLen, total);
                if (dashEnd > dashStart) {
                    float pieceLen = dashEnd - dashStart;
                    Vector2 mid = a + dir * ((dashStart + dashEnd) * 0.5f);
                    Vector2 pieceSize = new(pieceLen, pieceHeight);
                    Vector2 pieceXY = mid - pieceSize * 0.5f;
                    DrawRectangle(pieceXY, pieceSize, border, border, 0f, 0f, rotation, aaSize, dash: null);
                }
                t += period;
            }
        }

        public void DrawHexagon(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            if (dash != null && thickness > 0f) {
                DrawDashedHexagon(center, radius, fill, border, thickness, rounded, rotation, aaSize, dash.Value);
                return;
            }
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            rounded = MathF.Min(rounded, radius);

            float aaOffset = _pixelSize * aaSize;
            float radius1 = radius + aaOffset; // Account for AA.
            float width1 = 2f * radius / MathF.Sqrt(3f) + aaOffset; // Account for AA.

            radius -= rounded;

            Vector2 size = new(width1, radius1);

            var topLeft = center - size;
            var topRight = center + new Vector2(size.X, -size.Y);
            var bottomRight = center + size;
            var bottomLeft = center + new Vector2(-size.X, size.Y);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-size.X, -size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(size.X, -size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(size.X, size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-size.X, size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, aaSize: aaSize, rounded: rounded);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawHexagon(Vector2 center, float radius, Gradient fill, Color border, float thickness = 1f, float rounded = 0, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawHexagon(center, radius, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, rotation, aaSize, dash);
        }
        public void DrawHexagon(Vector2 center, float radius, Color fill, Gradient border, float thickness = 1f, float rounded = 0, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawHexagon(center, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, rounded, rotation, aaSize, dash);
        }
        public void DrawHexagon(Vector2 center, float radius, Color fill, Color border, float thickness = 1f, float rounded = 0, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawHexagon(center, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, rotation, aaSize, dash);
        }
        public void FillHexagon(Vector2 center, float radius, Gradient g, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawHexagon(center, radius, g, g, 0f, rounded, rotation, aaSize);
        }
        public void FillHexagon(Vector2 center, float radius, Color c, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawHexagon(center, radius, c, c, 0f, rounded, rotation, aaSize);
        }
        public void BorderHexagon(Vector2 center, float radius, Gradient g, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawHexagon(center, radius, Color.Transparent, g, thickness, rounded, rotation, aaSize, dash);
        }
        public void BorderHexagon(Vector2 center, float radius, Color c, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawHexagon(center, radius, Color.Transparent, c, thickness, rounded, rotation, aaSize, dash);
        }
        private void DrawDashedHexagon(Vector2 center, float apothem, Gradient fill, Gradient border, float thickness, float rounded, float rotation, float aaSize, DashPattern dash) {
            if (dash.Period <= 0f || dash.DashLength <= 0f || apothem <= 0f) return;
            rounded = MathF.Max(MathF.Min(rounded, apothem), 0f);
            float halfT = thickness * 0.5f;

            if (fill.IsLocal) GradientToWorld(ref fill, center, Vector2.Zero, rotation);
            fill.IsLocal = false;
            if (border.IsLocal) GradientToWorld(ref border, center, Vector2.Zero, rotation);
            border.IsLocal = false;

            // Inner fill: shrink the hexagon by `thickness` perpendicular to each edge.
            float innerApothem = MathF.Max(apothem - thickness, 0f);
            if (innerApothem > 0f) {
                float innerRounded = MathF.Max(rounded - thickness, 0f);
                DrawHexagon(center, innerApothem, fill, fill, 0f, innerRounded, rotation, aaSize, dash: null);
            }

            // Flat-top hexagon, walking edges clockwise from the top edge.
            // Edge directions (in unrotated local frame, screen y-down):
            //   e0 top:           (1, 0)
            //   e1 top-right:     (1/2,  √3/2)
            //   e2 bottom-right:  (-1/2, √3/2)
            //   e3 bottom:        (-1, 0)
            //   e4 bottom-left:   (-1/2, -√3/2)
            //   e5 top-left:      (1/2,  -√3/2)
            // Outward perp = math CW rotation = (dy, -dx).
            // Shrunken vertex angle θ_k = -60° + k·60° (between edges k and k+1, k=0..5).
            float sideEff = 2f * (apothem - rounded) / MathF.Sqrt(3f);
            float cornerArc = rounded * MathF.PI / 3f;
            float perimeter = 6f * sideEff + 6f * cornerArc;
            if (perimeter <= 0f) return;

            float ringRadius = MathF.Max(rounded - halfT, 0f);
            float shrunkenCircumradius = 2f * (apothem - rounded) / MathF.Sqrt(3f);

            float dashLen = dash.DashLength;
            float gapLen = dash.GapLength;
            float period = dash.Period;
            if (dash.FitToPath && perimeter > 0f) {
                int n = Math.Max(1, (int)MathF.Round(perimeter / period));
                float scale = perimeter / (n * period);
                dashLen *= scale;
                gapLen *= scale;
                period *= scale;
            }

            float t = -dash.PhaseOffset;
            t = ((t % period) + period) % period;
            t -= period;

            while (t < perimeter) {
                float dashStart = MathF.Max(t, 0f);
                float dashEnd = MathF.Min(t + dashLen, perimeter);
                if (dashEnd > dashStart) {
                    float segOff = 0f;
                    for (int k = 0; k < 6; k++) {
                        float edgeAngle = k * MathF.PI / 3f;
                        Vector2 dir = new(MathF.Cos(edgeAngle), MathF.Sin(edgeAngle));
                        Vector2 outwardPerp = new(dir.Y, -dir.X);
                        Vector2 inwardPerp = -outwardPerp;

                        float midAngle = edgeAngle - MathF.PI * 0.5f;
                        Vector2 edgeMid = center + new Vector2(MathF.Cos(midAngle) * apothem, MathF.Sin(midAngle) * apothem);
                        Vector2 edgeStart = edgeMid - dir * (sideEff * 0.5f);

                        EmitStripDash(dashStart, dashEnd, segOff, sideEff,
                            edgeStart, dir, inwardPerp, thickness, border, center, rotation, aaSize);
                        segOff += sideEff;

                        if (rounded > 0f) {
                            float vertexAngle = -MathF.PI / 3f + k * MathF.PI / 3f;
                            Vector2 cornerCenter = center + new Vector2(
                                MathF.Cos(vertexAngle) * shrunkenCircumradius,
                                MathF.Sin(vertexAngle) * shrunkenCircumradius);
                            float a1 = MathF.Atan2(outwardPerp.Y, outwardPerp.X);
                            float nextAngle = (k + 1) * MathF.PI / 3f;
                            Vector2 nextOut = new(MathF.Sin(nextAngle), -MathF.Cos(nextAngle));
                            float a2 = MathF.Atan2(nextOut.Y, nextOut.X);
                            EmitArcDash(dashStart, dashEnd, segOff, cornerArc,
                                cornerCenter, a1, a2, ringRadius, thickness,
                                border, center, rotation, aaSize);
                        }
                        segOff += cornerArc;
                    }
                }
                t += period;
            }
        }

        public void DrawEquilateralTriangle(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            if (dash != null && thickness > 0f) {
                DrawDashedEquilateralTriangle(center, radius, fill, border, thickness, rounded, rotation, aaSize, dash.Value);
                return;
            }
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            rounded = MathF.Min(rounded, radius);

            float aaOffset = _pixelSize * aaSize;
            float height = radius * 3f;

            float halfWidth = height / MathF.Sqrt(3f);
            float incircle = height / 3f;
            float circumcircle = 2f * height / 3f;

            float halfWidth1 = halfWidth + aaOffset; // Account for AA.
            float incircle1 = incircle + aaOffset; // Account for AA.
            float circumcircle1 = circumcircle + aaOffset; // Account for AA.

            halfWidth -= rounded * MathF.Sqrt(3f);

            var topLeft = center - new Vector2(halfWidth1, incircle1);
            var topRight = center + new Vector2(halfWidth1, -incircle1);
            var bottomRight = center + new Vector2(halfWidth1, circumcircle1);
            var bottomLeft = center + new Vector2(-halfWidth1, circumcircle1);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-halfWidth1, -incircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(halfWidth1, -incircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(halfWidth1, circumcircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, aaSize: aaSize, rounded: rounded);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-halfWidth1, circumcircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, aaSize: aaSize, rounded: rounded);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawEquilateralTriangle(Vector2 center, float radius, Gradient fill, Color border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawEquilateralTriangle(center, radius, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, rotation, aaSize, dash);
        }
        public void DrawEquilateralTriangle(Vector2 center, float radius, Color fill, Gradient border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawEquilateralTriangle(center, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, rounded, rotation, aaSize, dash);
        }
        public void DrawEquilateralTriangle(Vector2 center, float radius, Color fill, Color border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawEquilateralTriangle(center, radius, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, rotation, aaSize, dash);
        }
        public void FillEquilateralTriangle(Vector2 center, float radius, Gradient g, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEquilateralTriangle(center, radius, g, g, 0f, rounded, rotation, aaSize);
        }
        public void FillEquilateralTriangle(Vector2 center, float radius, Color c, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEquilateralTriangle(center, radius, c, c, 0f, rounded, rotation, aaSize);
        }
        public void BorderEquilateralTriangle(Vector2 center, float radius, Gradient g, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawEquilateralTriangle(center, radius, Color.Transparent, g, thickness, rounded, rotation, aaSize, dash);
        }
        public void BorderEquilateralTriangle(Vector2 center, float radius, Color c, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawEquilateralTriangle(center, radius, Color.Transparent, c, thickness, rounded, rotation, aaSize, dash);
        }
        private void DrawDashedEquilateralTriangle(Vector2 center, float apothem, Gradient fill, Gradient border, float thickness, float rounded, float rotation, float aaSize, DashPattern dash) {
            if (dash.Period <= 0f || dash.DashLength <= 0f || apothem <= 0f) return;
            rounded = MathF.Max(MathF.Min(rounded, apothem), 0f);
            float halfT = thickness * 0.5f;

            if (fill.IsLocal) GradientToWorld(ref fill, center, Vector2.Zero, rotation);
            fill.IsLocal = false;
            if (border.IsLocal) GradientToWorld(ref border, center, Vector2.Zero, rotation);
            border.IsLocal = false;

            // Inner fill
            float innerApothem = MathF.Max(apothem - thickness, 0f);
            if (innerApothem > 0f) {
                float innerRounded = MathF.Max(rounded - thickness, 0f);
                DrawEquilateralTriangle(center, innerApothem, fill, fill, 0f, innerRounded, rotation, aaSize, dash: null);
            }

            // Apex-down equilateral triangle (matching DrawEquilateralTriangle's
            // bounding box: incircle above center, circumcircle below). Walking
            // v0 → v1 → v2 (apex → top-left → top-right) is visual-CW; outward
            // perp = (dy, -dx).
            float sqrt3 = MathF.Sqrt(3f);
            Vector2 v0 = center + new Vector2(0f, 2f * apothem);
            Vector2 v1 = center + new Vector2(-apothem * sqrt3, -apothem);
            Vector2 v2 = center + new Vector2(apothem * sqrt3, -apothem);

            // Shrunken vertices (corner arc centers) — scale toward incenter (= center).
            float shrinkRatio = (apothem - rounded) / apothem;
            Vector2 v0S = center + (v0 - center) * shrinkRatio;
            Vector2 v1S = center + (v1 - center) * shrinkRatio;
            Vector2 v2S = center + (v2 - center) * shrinkRatio;
            Vector2[] vertsShrunk = { v0S, v1S, v2S };

            // Build edge data for the 3 edges (v0→v1, v1→v2, v2→v0).
            float sideEff = 2f * sqrt3 * (apothem - rounded);
            float cornerArc = rounded * 2f * MathF.PI / 3f;
            float perimeter = 3f * sideEff + 3f * cornerArc;
            if (perimeter <= 0f) return;

            float ringRadius = MathF.Max(rounded - halfT, 0f);

            Vector2[] edgeStarts = new Vector2[3];
            Vector2[] edgeDirs = new Vector2[3];
            Vector2[] inwardPerps = new Vector2[3];
            float[] outwardAngles = new float[3];
            for (int i = 0; i < 3; i++) {
                Vector2 vs = vertsShrunk[i];
                Vector2 ve = vertsShrunk[(i + 1) % 3];
                Vector2 d = ve - vs;
                float len = d.Length();
                if (len <= 0f) return;
                d /= len;
                edgeDirs[i] = d;
                Vector2 outwardPerp = new(d.Y, -d.X);
                inwardPerps[i] = -outwardPerp;
                edgeStarts[i] = vs + outwardPerp * rounded;
                outwardAngles[i] = MathF.Atan2(outwardPerp.Y, outwardPerp.X);
            }

            float dashLen = dash.DashLength;
            float gapLen = dash.GapLength;
            float period = dash.Period;
            if (dash.FitToPath && perimeter > 0f) {
                int n = Math.Max(1, (int)MathF.Round(perimeter / period));
                float scale = perimeter / (n * period);
                dashLen *= scale;
                gapLen *= scale;
                period *= scale;
            }

            float t = -dash.PhaseOffset;
            t = ((t % period) + period) % period;
            t -= period;

            while (t < perimeter) {
                float dashStart = MathF.Max(t, 0f);
                float dashEnd = MathF.Min(t + dashLen, perimeter);
                if (dashEnd > dashStart) {
                    float segOff = 0f;
                    for (int i = 0; i < 3; i++) {
                        EmitStripDash(dashStart, dashEnd, segOff, sideEff,
                            edgeStarts[i], edgeDirs[i], inwardPerps[i],
                            thickness, border, center, rotation, aaSize);
                        segOff += sideEff;

                        if (rounded > 0f) {
                            int next = (i + 1) % 3;
                            EmitArcDash(dashStart, dashEnd, segOff, cornerArc,
                                vertsShrunk[next], outwardAngles[i], outwardAngles[next],
                                ringRadius, thickness,
                                border, center, rotation, aaSize);
                        }
                        segOff += cornerArc;
                    }
                }
                t += period;
            }
        }

        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            if (dash != null && thickness > 0f) {
                DrawDashedTriangle(a, b, c, fill, border, thickness, rounded, aaSize, dash.Value);
                return;
            }
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            GradientToWorld(ref fill, ref border, a, Vector2.Zero, MathF.Atan2(b.Y - a.Y, b.X - a.X));

            float aaOffset = _pixelSize * aaSize;
            float winding = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (winding > 0) {
                (b, c) = (c, b);
            }

            float sideA = Vector2.Distance(a, b);
            float sideB = Vector2.Distance(b, c);
            float sideC = Vector2.Distance(c, a);

            float longestSide;

            Vector2 A;
            Vector2 B;
            Vector2 C;

            if (sideA > sideB && sideA > sideC) {
                longestSide = sideA;
                A = a;
                B = b;
            } else if (sideB > sideC) {
                longestSide = sideB;
                A = b;
                B = c;
            } else {
                longestSide = sideC;
                A = c;
                B = a;
            }

            float area = 0.5f * MathF.Abs(a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            float height = 2f * area / longestSide;

            float offset = aaOffset;

            var D = Slide(B, A, offset);
            var E = Slide(A, B, offset);

            var topLeft = Clockwise(E, D, offset);
            var topRight = CounterClockwise(D, E, offset);
            var bottomRight = Clockwise(D, E, height + offset);
            var bottomLeft = CounterClockwise(E, D, height + offset);

            float inCenterX = (sideB * a.X + sideC * b.X + sideA * c.X) / (sideB + sideC + sideA);
            float inCenterY = (sideB * a.Y + sideC * b.Y + sideA * c.Y) / (sideB + sideC + sideA);
            float inRadius = MathF.Sqrt((-sideB + sideC + sideA) * (sideB - sideC + sideA) * (sideB + sideC - sideA) / (sideB + sideC + sideA)) / 2f;
            float ratioDistance = (inRadius - rounded) / inRadius;

            if (ratioDistance < 0.001f) {
                ratioDistance = 0.001f;
                rounded = inRadius - inRadius * ratioDistance;
            }

            A = new Vector2(inCenterX + (ratioDistance * (a.X - inCenterX)), inCenterY + (ratioDistance * (a.Y - inCenterY)));
            B = new Vector2(inCenterX + (ratioDistance * (b.X - inCenterX)), inCenterY + (ratioDistance * (b.Y - inCenterY)));
            C = new Vector2(inCenterX + (ratioDistance * (c.X - inCenterX)), inCenterY + (ratioDistance * (c.Y - inCenterY)));

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), topLeft, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), topRight, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), bottomRight, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), bottomLeft, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient fill, Color border, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawTriangle(a, b, c, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, aaSize, dash);
        }
        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color fill, Gradient border, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawTriangle(a, b, c, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, rounded, aaSize, dash);
        }
        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color fill, Color border, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawTriangle(a, b, c, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rounded, aaSize, dash);
        }
        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient g, float rounded = 0f, float aaSize = 1.5f) {
            DrawTriangle(a, b, c, g, g, 0f, rounded, aaSize);
        }
        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color c1, float rounded = 0f, float aaSize = 1.5f) {
            DrawTriangle(a, b, c, c1, c1, 0f, rounded, aaSize);
        }
        public void BorderTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient g, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawTriangle(a, b, c, Color.Transparent, g, thickness, rounded, aaSize, dash);
        }
        public void BorderTriangle(Vector2 a, Vector2 b, Vector2 c, Color c1, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashPattern? dash = null) {
            DrawTriangle(a, b, c, Color.Transparent, c1, thickness, rounded, aaSize, dash);
        }
        private void DrawDashedTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient fill, Gradient border, float thickness, float rounded, float aaSize, DashPattern dash) {
            if (dash.Period <= 0f || dash.DashLength <= 0f) return;

            // Force visual-CW winding (in screen y-down) so the inward perpendicular
            // formula and corner sweep direction match the rectangle and hexagon.
            float winding = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (winding < 0f) {
                (b, c) = (c, b);
            } else if (winding == 0f) {
                return; // degenerate triangle
            }

            float sideAB = Vector2.Distance(a, b);
            float sideBC = Vector2.Distance(b, c);
            float sideCA = Vector2.Distance(c, a);
            if (sideAB <= 0f || sideBC <= 0f || sideCA <= 0f) return;

            float perimSum = sideAB + sideBC + sideCA;
            Vector2 inCenter = new(
                (sideBC * a.X + sideCA * b.X + sideAB * c.X) / perimSum,
                (sideBC * a.Y + sideCA * b.Y + sideAB * c.Y) / perimSum);

            float inRadius = MathF.Sqrt(
                (-sideBC + sideCA + sideAB) *
                (sideBC - sideCA + sideAB) *
                (sideBC + sideCA - sideAB) / perimSum) / 2f;
            rounded = MathF.Max(MathF.Min(rounded, inRadius * 0.999f), 0f);
            float halfT = thickness * 0.5f;

            float gradRotation = MathF.Atan2(b.Y - a.Y, b.X - a.X);
            if (fill.IsLocal) GradientToWorld(ref fill, a, Vector2.Zero, gradRotation);
            fill.IsLocal = false;
            if (border.IsLocal) GradientToWorld(ref border, a, Vector2.Zero, gradRotation);
            border.IsLocal = false;

            // Inner fill: scale vertices toward the incenter so the inner triangle's
            // edges are perpendicular distance `thickness` inside the original edges.
            float fillRatio = MathF.Max((inRadius - thickness) / inRadius, 0f);
            if (fillRatio > 0f) {
                Vector2 aF = inCenter + fillRatio * (a - inCenter);
                Vector2 bF = inCenter + fillRatio * (b - inCenter);
                Vector2 cF = inCenter + fillRatio * (c - inCenter);
                float innerRounded = MathF.Max(rounded - thickness, 0f);
                DrawTriangle(aF, bF, cF, fill, fill, 0f, innerRounded, aaSize, dash: null);
            }

            // Shrunken triangle: vertices scaled toward incenter so each edge is
            // `rounded` perpendicular distance inside the original edge. The
            // shrunken vertices are the corner arc centers.
            float shrinkRatio = (inRadius - rounded) / inRadius;
            Vector2 aS = inCenter + shrinkRatio * (a - inCenter);
            Vector2 bS = inCenter + shrinkRatio * (b - inCenter);
            Vector2 cS = inCenter + shrinkRatio * (c - inCenter);
            Vector2[] vertsShrunk = { aS, bS, cS };

            // Build edge data
            Vector2[] edgeStarts = new Vector2[3];
            Vector2[] edgeDirs = new Vector2[3];
            Vector2[] inwardPerps = new Vector2[3];
            float[] outwardAngles = new float[3];
            float[] edgeLengths = new float[3];
            for (int i = 0; i < 3; i++) {
                Vector2 vs = vertsShrunk[i];
                Vector2 ve = vertsShrunk[(i + 1) % 3];
                Vector2 d = ve - vs;
                float len = d.Length();
                if (len <= 0f) return;
                d /= len;
                edgeDirs[i] = d;
                Vector2 outwardPerp = new(d.Y, -d.X);
                inwardPerps[i] = -outwardPerp;
                edgeStarts[i] = vs + outwardPerp * rounded;
                Vector2 edgeEnd = ve + outwardPerp * rounded;
                edgeLengths[i] = (edgeEnd - edgeStarts[i]).Length();
                outwardAngles[i] = MathF.Atan2(outwardPerp.Y, outwardPerp.X);
            }

            // Corner arc lengths (per vertex)
            float[] cornerArcLens = new float[3];
            for (int i = 0; i < 3; i++) {
                int next = (i + 1) % 3;
                float delta = outwardAngles[next] - outwardAngles[i];
                while (delta > MathF.PI) delta -= 2f * MathF.PI;
                while (delta < -MathF.PI) delta += 2f * MathF.PI;
                cornerArcLens[i] = rounded * MathF.Abs(delta);
            }

            float perimeter = 0f;
            for (int i = 0; i < 3; i++) perimeter += edgeLengths[i] + cornerArcLens[i];
            if (perimeter <= 0f) return;

            float ringRadius = MathF.Max(rounded - halfT, 0f);

            float dashLen = dash.DashLength;
            float gapLen = dash.GapLength;
            float period = dash.Period;
            if (dash.FitToPath && perimeter > 0f) {
                int n = Math.Max(1, (int)MathF.Round(perimeter / period));
                float scale = perimeter / (n * period);
                dashLen *= scale;
                gapLen *= scale;
                period *= scale;
            }

            float t = -dash.PhaseOffset;
            t = ((t % period) + period) % period;
            t -= period;

            while (t < perimeter) {
                float dashStart = MathF.Max(t, 0f);
                float dashEnd = MathF.Min(t + dashLen, perimeter);
                if (dashEnd > dashStart) {
                    float segOff = 0f;
                    for (int i = 0; i < 3; i++) {
                        EmitStripDash(dashStart, dashEnd, segOff, edgeLengths[i],
                            edgeStarts[i], edgeDirs[i], inwardPerps[i],
                            thickness, border, Vector2.Zero, 0f, aaSize);
                        segOff += edgeLengths[i];

                        if (rounded > 0f && cornerArcLens[i] > 0f) {
                            int next = (i + 1) % 3;
                            EmitArcDash(dashStart, dashEnd, segOff, cornerArcLens[i],
                                vertsShrunk[next], outwardAngles[i], outwardAngles[next],
                                ringRadius, thickness,
                                border, Vector2.Zero, 0f, aaSize);
                        }
                        segOff += cornerArcLens[i];
                    }
                }
                t += period;
            }
        }

        public void DrawEllipse(Vector2 center, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            float aaOffset = _pixelSize * aaSize;
            float radius3 = radius1 + aaOffset; // Account for AA.
            float radius4 = radius2 + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius3, -radius4);
            var topRight = center + new Vector2(radius3, -radius4);
            var bottomRight = center + new Vector2(radius3, radius4);
            var bottomLeft = center + new Vector2(-radius3, radius4);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, radius2, aaSize: aaSize);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, radius2, aaSize: aaSize);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, radius2, aaSize: aaSize);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, radius2, aaSize: aaSize);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawEllipse(Vector2 center, float radius1, float radius2, Gradient fill, Color border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, radius1, radius2, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rotation, aaSize);
        }
        public void DrawEllipse(Vector2 center, float radius1, float radius2, Color fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, radius1, radius2, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, rotation, aaSize);
        }
        public void DrawEllipse(Vector2 center, float radius1, float radius2, Color fill, Color border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, radius1, radius2, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, rotation, aaSize);
        }
        public void FillEllipse(Vector2 center, float width, float height, Gradient g, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, width, height, g, g, 0f, rotation, aaSize);
        }
        public void FillEllipse(Vector2 center, float width, float height, Color c, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, width, height, c, c, 0f, rotation, aaSize);
        }
        public void BorderEllipse(Vector2 center, float width, float height, Gradient g, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, width, height, Color.Transparent, g, thickness, rotation, aaSize);
        }
        public void BorderEllipse(Vector2 center, float width, float height, Color c, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, width, height, Color.Transparent, c, thickness, rotation, aaSize);
        }

        public void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            radius1 -= 1f;

            float angleSize = MathF.Abs(Mod((angle2 - angle1) * 0.5f + MathF.PI, MathF.PI * 2f) - MathF.PI);
            float sin = MathF.Sin(angleSize);
            float cos = MathF.Cos(angleSize);

            float aaOffset = _pixelSize * aaSize;
            float radius3 = radius1 + radius2 + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius3);
            var topRight = center + new Vector2(radius3, -radius3);
            var bottomRight = center + new Vector2(radius3);
            var bottomLeft = center + new Vector2(-radius3, radius3);

            float rotation = (angle1 + angle2 - MathF.PI) * 0.5f;

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, angle1);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: sin, b: cos, c: radius2);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: sin, b: cos, c: radius2);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: sin, b: cos, c: radius2);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: sin, b: cos, c: radius2);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Color border, float thickness = 1f, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, aaSize);
        }
        public void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color fill, Gradient border, float thickness = 1f, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, aaSize);
        }
        public void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color fill, Color border, float thickness = 1f, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, aaSize);
        }
        public void FillArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, g, g, 0f, aaSize);
        }
        public void FillArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color c, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, c, c, 0f, aaSize);
        }
        public void BorderArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float thickness = 1f, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, Color.Transparent, g, thickness, aaSize);
        }
        public void BorderArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color c, float thickness = 1f, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, Color.Transparent, c, thickness, aaSize);
        }

        public void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            radius1 -= 1f;

            float angleSize = MathF.Abs(Mod((angle2 - angle1) * 0.5f + MathF.PI, MathF.PI * 2f) - MathF.PI);

            float cos = MathF.Cos(angleSize);
            float sin = MathF.Sin(angleSize);

            float aaOffset = _pixelSize * aaSize;
            float radius3 = radius1 + radius2 + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius3);
            var topRight = center + new Vector2(radius3, -radius3);
            var bottomRight = center + new Vector2(radius3);
            var bottomLeft = center + new Vector2(-radius3, radius3);

            float rotation = (angle1 + angle2 - MathF.PI) * 0.5f;

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, angle1);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: cos, b: sin, c: radius2);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: cos, b: sin, c: radius2);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: cos, b: sin, c: radius2);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, aaSize: aaSize, a: cos, b: sin, c: radius2);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Color border, float thickness = 1f, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, fill, new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, aaSize);
        }
        public void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color fill, Gradient border, float thickness = 1f, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), border, thickness, aaSize);
        }
        public void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color fill, Color border, float thickness = 1f, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, new Gradient(Vector2.Zero, fill, Vector2.Zero, fill, Gradient.Shape.None), new Gradient(Vector2.Zero, border, Vector2.Zero, border, Gradient.Shape.None), thickness, aaSize);
        }
        public void FillRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, g, g, 0f, aaSize);
        }
        public void FillRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color c, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, c, c, 0f, aaSize);
        }
        public void BorderRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float thickness = 1f, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, Color.Transparent, g, thickness, aaSize);
        }
        public void BorderRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Color c, float thickness = 1f, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, Color.Transparent, c, thickness, aaSize);
        }

        public void Draw(Texture2D texture, Matrix3x2 world, Matrix3x2? source = null, Color? mask = null) {
            if (_texture == null) {
                _texture = texture;
            } else if (_texture != texture) {
                Flush();
                _texture = texture;
            }

            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            Vector2 topLeft;
            Vector2 topRight;
            Vector2 bottomRight;
            Vector2 bottomLeft;
            if (source == null) {
                topLeft = new Vector2(0, 0);
                topRight = new Vector2(texture.Width, 0);
                bottomRight = new Vector2(texture.Width, texture.Height);
                bottomLeft = new Vector2(0, texture.Height);
            } else {
                topLeft = Vector2.Transform(new Vector2(0f, 0f), source.Value);
                topRight = Vector2.Transform(new Vector2(1f, 0f), source.Value);
                bottomRight = Vector2.Transform(new Vector2(1f, 1f), source.Value);
                bottomLeft = Vector2.Transform(new Vector2(0, 1f), source.Value);
            }

            Vector2 wTopLeft = Vector2.Transform(new Vector2(0, 0), world);
            Vector2 wTopRight = Vector2.Transform(new Vector2(1f, 0), world);
            Vector2 wBottomRight = Vector2.Transform(new Vector2(1f, 1f), world);
            Vector2 wBottomLeft = Vector2.Transform(new Vector2(0, 1f), world);

            Gradient g = new(Vector2.Zero, mask ?? Color.White, Vector2.Zero, mask ?? Color.White, Gradient.Shape.None);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(wTopLeft.X, wTopLeft.Y, 0f), GetUV(texture, topLeft), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(wTopRight.X, wTopRight.Y, 0f), GetUV(texture, topRight), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(wBottomRight.X, wBottomRight.Y, 0f), GetUV(texture, bottomRight), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(wBottomLeft.X, wBottomLeft.Y, 0f), GetUV(texture, bottomLeft), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void Draw(Texture2D texture, Vector2 xy) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(xy));
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(xy), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(xy), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, Vector2 scale) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, float scale) {
            Draw(texture, xy, mask, rotation, origin, new Vector2(scale));
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, Vector2 scale) {
            Draw(texture, Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, float scale) {
            Draw(texture, xy, source, mask, rotation, origin, new Vector2(scale));
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(texture.Width, texture.Height) : Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, float scale, SpriteEffects effects) {
            Draw(texture, xy, mask, rotation, origin, new Vector2(scale), effects);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position) : Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, float scale, SpriteEffects effects) {
            Draw(texture, xy, source, mask, rotation, origin, new Vector2(scale), effects);
        }
        public void Draw(Texture2D texture, RectangleF destination) {
            Draw(texture, Matrix3x2.CreateScale(destination.Width, destination.Height) * Matrix3x2.CreateTranslation(destination.Position));
        }
        public void Draw(Texture2D texture, RectangleF destination, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(destination.Width, destination.Height) * Matrix3x2.CreateTranslation(destination.Position), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, RectangleF source, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(destination.Width, destination.Height) * Matrix3x2.CreateTranslation(destination.Position), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, Color mask, float rotation, Vector2 origin) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, RectangleF source, Color mask, float rotation, Vector2 origin) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, Color mask, float rotation, Vector2 origin, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(texture.Width, texture.Height) : Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, RectangleF source, Color mask, float rotation, Vector2 origin, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position) : Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }

        public float DrawString(SpriteFontBase font, string text, Vector2 position, Color color, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, color, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, string text, Vector2 position, Color[] colors, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, colors, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringSegment text, Vector2 position, Color color, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, color, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringSegment text, Vector2 position, Color[] colors, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, colors, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringBuilder text, Vector2 position, Color color, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, color, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringBuilder text, Vector2 position, Color[] colors, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, colors, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }

        private void DrawStringTexture(Texture2D texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight) {
            if (_fontTexture == null) {
                _fontTexture = texture;
            } else if (_fontTexture != texture) {
                Flush();
                _fontTexture = texture;
            }

            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            Gradient gTopLeft = new(Vector2.Zero, topLeft.Color, Vector2.Zero, topLeft.Color, Gradient.Shape.None);
            Gradient gTopRight = new(Vector2.Zero, topRight.Color, Vector2.Zero, topRight.Color, Gradient.Shape.None);
            Gradient gBottomRight = new(Vector2.Zero, bottomRight.Color, Vector2.Zero, bottomRight.Color, Gradient.Shape.None);
            Gradient gBottomLeft = new(Vector2.Zero, bottomLeft.Color, Vector2.Zero, bottomLeft.Color, Gradient.Shape.None);

            _vertices[_vertexCount + 0] = new VertexShape(topLeft.Position, topLeft.TextureCoordinate, VertexShape.Shape.String, gTopLeft, gTopLeft, 0f, 1f, _pixelSize);
            _vertices[_vertexCount + 1] = new VertexShape(topRight.Position, topRight.TextureCoordinate, VertexShape.Shape.String, gTopRight, gTopRight, 0f, 1f, _pixelSize);
            _vertices[_vertexCount + 2] = new VertexShape(bottomRight.Position, bottomRight.TextureCoordinate, VertexShape.Shape.String, gBottomRight, gBottomRight, 0f, 1f, _pixelSize);
            _vertices[_vertexCount + 3] = new VertexShape(bottomLeft.Position, bottomLeft.TextureCoordinate, VertexShape.Shape.String, gBottomLeft, gBottomLeft, 0f, 1f, _pixelSize);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }

        public void End() {
            Flush();

            // TODO: Restore old states like rasterizer, depth stencil, blend state?
        }

        private void Flush() {
            if (_triangleCount == 0) return;

            _effect.Parameters["view_projection"].SetValue(_view * _projection);

            if (_indicesChanged) {
                _vertexBuffer.Dispose();
                _indexBuffer.Dispose();

                _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

                GenerateIndexArray();

                _indexBuffer = new IndexBuffer(_graphicsDevice, typeof(uint), _indices.Length, BufferUsage.WriteOnly);
                _indexBuffer.SetData(_indices);

                _indicesChanged = false;
            }

            _vertexBuffer.SetData(_vertices);
            _graphicsDevice.SetVertexBuffer(_vertexBuffer);

            _graphicsDevice.Indices = _indexBuffer;

            _graphicsDevice.BlendState = _blendState;
            _graphicsDevice.SamplerStates[0] = _samplerState;
            _graphicsDevice.DepthStencilState = _depthStencilState;
            _graphicsDevice.RasterizerState = _rasterizerState;

            foreach (EffectPass pass in _effect.CurrentTechnique.Passes) {
                pass.Apply();
                if (_texture != null) _graphicsDevice.Textures[0] = _texture;
                if (_fontTexture != null) _graphicsDevice.Textures[1] = _fontTexture;

                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _triangleCount);
            }

            _triangleCount = 0;
            _vertexCount = 0;
            _indexCount = 0;
        }
        private float ScreenToWorldScale() {
            return Vector2.Distance(ScreenToWorld(0f, 0f), ScreenToWorld(1f, 0f));
        }
        private Vector2 ScreenToWorld(float x, float y) {
            return ScreenToWorld(new Vector2(x, y));
        }
        private Vector2 ScreenToWorld(Vector2 xy) {
            return Vector2.Transform(xy, Matrix.Invert(_view));
        }

        private static Vector2 Slide(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return b + c;
        }
        private static Vector2 Clockwise(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return new Vector2(c.Y, -c.X) + a;
        }
        private static Vector2 CounterClockwise(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return new Vector2(-c.Y, c.X) + a;
        }
        private static Vector2 Rotate(Vector2 a, Vector2 origin, float rotation) {
            return new Vector2(origin.X + (a.X - origin.X) * MathF.Cos(rotation) - (a.Y - origin.Y) * MathF.Sin(rotation), origin.Y + (a.X - origin.X) * MathF.Sin(rotation) + (a.Y - origin.Y) * MathF.Cos(rotation));
        }
        private static float Mod(float x, float m) {
            return (x % m + m) % m;
        }
        private static void GradientToWorld(ref Gradient g1, ref Gradient g2, Vector2 center, Vector2 offset, float rotation) {
            if (g1.IsLocal) GradientToWorld(ref g1, center, offset, rotation);
            if (g2.IsLocal) GradientToWorld(ref g2, center, offset, rotation);
        }
        private static void GradientToWorld(ref Gradient g, Vector2 center, Vector2 offset, float rotation) {
            g.AXY = Rotate(g.AXY + offset, Vector2.Zero, rotation);
            g.BXY = Rotate(g.BXY + offset, Vector2.Zero, rotation);

            g.AXY += center;
            g.BXY += center;
        }

        private static Vector2 GetUV(Texture2D texture, Vector2 xy) {
            return new Vector2(xy.X / texture.Width, xy.Y / texture.Height);
        }

        private static bool EnsureSizeOrDouble<T>(ref T[] array, int neededCapacity) {
            if (array.Length < neededCapacity) {
                Array.Resize(ref array, array.Length * 2);
                return true;
            }
            return false;
        }

        private void GenerateIndexArray() {
            for (uint i = _fromIndex, j = _fromVertex; i < _indices.Length; i += 6, j += 4) {
                _indices[i + 0] = j + 0;
                _indices[i + 1] = j + 1;
                _indices[i + 2] = j + 3;
                _indices[i + 3] = j + 1;
                _indices[i + 4] = j + 2;
                _indices[i + 5] = j + 3;
            }
            _fromIndex = (uint)_indices.Length;
            _fromVertex = (uint)_vertices.Length;
        }

        private class FontStashRenderer(GraphicsDevice gd, ShapeBatch sb) : IFontStashRenderer2 {
            public GraphicsDevice GraphicsDevice => _graphicsDevice;

            public void DrawQuad(Texture2D texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight) {
                _sb.DrawStringTexture(texture, ref topLeft, ref topRight, ref bottomLeft, ref bottomRight);
            }

            readonly GraphicsDevice _graphicsDevice = gd;
            readonly ShapeBatch _sb = sb;
        }

        private Texture2D? _texture = null;
        private Texture2D? _fontTexture = null;

        private const int _initialVertices = 2048 * 4;
        private const int _initialIndices = 2048 * 6;

        private readonly GraphicsDevice _graphicsDevice;
        private VertexShape[] _vertices;
        private uint[] _indices;
        private int _triangleCount = 0;
        private int _vertexCount = 0;
        private int _indexCount = 0;

        private DynamicVertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;

        private BlendState _blendState = null!;
        private SamplerState _samplerState = null!;
        private DepthStencilState _depthStencilState = null!;
        private RasterizerState _rasterizerState = null!;

        private Matrix _view;
        private Matrix _projection;
        private readonly Effect _effect;

        private float _pixelSize = 1f;

        private bool _indicesChanged = false;
        private uint _fromIndex = 0;
        private uint _fromVertex = 0;

        private readonly FontStashRenderer _fsr;
    }
}
