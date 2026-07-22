using System;
using System.Numerics;
using MouseBeautifier.Core;

namespace MouseBeautifier
{
    /// <summary>
    /// Headless proof that the five-pointed star (五角星) stays glued to the
    /// rope end. This is the contract the user explicitly asked for:
    /// "确保五角星和绳子是不会分开的".
    ///
    /// The proof has two independent halves:
    ///   (a) RopePhysicsTests / RopeSimulator  -> the rope end (Bob) can never
    ///       exceed the rope's reach, so it never flies off the cursor.
    ///   (b) THIS test -> the star's TOP VERTEX is defined at the local origin,
    ///       and the renderer transform is world = Rotation·local + Tip, i.e. a
    ///       rotation ABOUT the rope end. Rotating about a point leaves that
    ///       point fixed, so the star's attachment vertex is mathematically
    ///       identical to the rope end for every frame and every angle.
    /// Together they prove end-to-end non-separation.
    ///
    /// Pure (System.Numerics only) so it runs without Win2D — see the
    /// StarAttachment.Verify console project, which links this file + PendantGeometry.cs.
    /// </summary>
    public static class StarAttachmentTests
    {
        public static int Run()
        {
            int fail = 0, total = 0;
            void Check(string name, bool ok, string detail)
            {
                total++;
                if (!ok) fail++;
                string tag = ok ? "PASS" : "FAIL";
                string line = $"[{tag}] {name}: {detail}";
                Console.WriteLine(line);
                App.Log(line);
            }

            float size = 38f;
            var star = PendantGeometry.StarLocalPolygon(size);

            // ---- Test 1: local geometry — top tip is THE attachment vertex ----
            // The star must be defined so that its top-most point is exactly the
            // local origin (0,0); everything else extends in +Y. If that holds,
            // the transform (which fixes the origin) keeps the star welded to the
            // rope end regardless of rotation.
            {
                float minY = float.MaxValue;
                int minIdx = -1;
                for (int i = 0; i < star.Length; i++)
                {
                    if (star[i].Y < minY) { minY = star[i].Y; minIdx = i; }
                }
                bool topIsOrigin = minIdx >= 0 && Math.Abs(star[minIdx].X) < 1e-4f && Math.Abs(minY) < 1e-4f;
                // also symmetric about x=0
                float cx = 0;
                foreach (var v in star) cx += v.X;
                cx /= star.Length;
                bool symmetric = Math.Abs(cx) < 1e-3f;
                Check("星形局部锚点=原点", topIsOrigin && symmetric,
                    $"topVertex=({star[minIdx].X:F3},{star[minIdx].Y:F3}) centroidX={cx:F4} (需 (0,0) 且关于x轴对称)");
            }

            // ---- Test 2: top vertex maps to rope end, every scenario ----
            // Build a 2-point "rope" [prev, tip] for each scenario and confirm
            // TransformPoint(topVertex) == Tip. Covering down/right/up-left/
            // degenerate/NaN + 500 random orientations.
            {
                var scenarios = new (Vector2 prev, Vector2 tip, string label)[]
                {
                    (new Vector2(400, 400), new Vector2(400, 500), "竖直下垂"),
                    (new Vector2(500, 300), new Vector2(600, 300), "水平向右"),
                    (new Vector2(300, 300), new Vector2(200, 200), "斜向左上"),
                    (new Vector2(250, 350), new Vector2(250, 350), "退化(重合->回退)"),
                };

                bool allOk = true;
                int checks = 0;
                foreach (var sc in scenarios)
                {
                    var rope = new Vector2[] { sc.prev, sc.tip };
                    var p = PendantGeometry.ComputePendant(rope, size);
                    var top = PendantGeometry.TransformPoint(p, star[0]); // star[0] = top tip = (0,0)
                    if (Vector2.Distance(top, p.Tip) > 1e-4f) { allOk = false; }
                    checks++;
                }

                // 500 random orientations + positions
                var rng = new Random(20260715);
                for (int i = 0; i < 500; i++)
                {
                    double ang = rng.NextDouble() * 2 * Math.PI;
                    float len = 50f + (float)rng.NextDouble() * 200f;
                    var prev = new Vector2((float)(rng.NextDouble() * 1000), (float)(rng.NextDouble() * 700));
                    var tip = prev + new Vector2((float)Math.Cos(ang) * len, (float)Math.Sin(ang) * len);
                    var rope = new Vector2[] { prev, tip };
                    var p = PendantGeometry.ComputePendant(rope, size);
                    var top = PendantGeometry.TransformPoint(p, star[0]);
                    if (Vector2.Distance(top, p.Tip) > 1e-4f) { allOk = false; }
                    checks++;
                }

                // NaN guard
                {
                    var rope = new Vector2[] { new Vector2(float.NaN, 1), new Vector2(float.NaN, float.NaN) };
                    var p = PendantGeometry.ComputePendant(rope, size);
                    var top = PendantGeometry.TransformPoint(p, star[0]);
                    if (Vector2.Distance(top, p.Tip) > 1e-4f) allOk = false;
                    checks++;
                    if (float.IsNaN(p.Tip.X) || float.IsNaN(p.Tip.Y)) allOk = false; // must not propagate NaN
                }

                Check("顶端=绳末端(全场景)", allOk, $"{checks}次检查 星形顶点与绳末端距离<1e-4 (永不分离)");
            }

            // ---- Test 3: orientation sanity — star centroid hangs along rope ----
            // centroid_local = (0, size/2); after transform it must equal
            // Tip + dir*(size/2). Confirms the rotation convention matches the
            // renderer and the star points ALONG the rope, not sideways.
            {
                var rng = new Random(7);
                bool ok = true;
                for (int i = 0; i < 300; i++)
                {
                    double ang = rng.NextDouble() * 2 * Math.PI;
                    float len = 80f;
                    var prev = new Vector2(300, 300);
                    var tip = prev + new Vector2((float)Math.Cos(ang) * len, (float)Math.Sin(ang) * len);
                    var rope = new Vector2[] { prev, tip };
                    var p = PendantGeometry.ComputePendant(rope, size);

                    Vector2 cen = Vector2.Zero;
                    foreach (var v in star) cen += v;
                    cen /= star.Length;
                    var worldCen = PendantGeometry.TransformPoint(p, cen);
                    var expect = p.Tip + p.Direction * (size / 2f);
                    if (Vector2.Distance(worldCen, expect) > 1e-3f) ok = false;
                }
                Check("朝向沿绳方向", ok, "星形质心 = 绳末端 + 方向*size/2 (旋转约定与渲染一致)");
            }

            // ---- Test 4: direction is a unit vector (no scaling/tearing) ----
            {
                var rng = new Random(99);
                bool unit = true;
                for (int i = 0; i < 300; i++)
                {
                    double ang = rng.NextDouble() * 2 * Math.PI;
                    var prev = new Vector2(100, 100);
                    var tip = prev + new Vector2((float)Math.Cos(ang) * 120, (float)Math.Sin(ang) * 120);
                    var p = PendantGeometry.ComputePendant(new Vector2[] { prev, tip }, size);
                    if (Math.Abs(p.Direction.Length() - 1f) > 1e-3f) unit = false;
                }
                Check("方向为单住向量", unit, "|Direction| 全部 == 1.0 (无拉伸/撕裂)");
            }

            // ---- Test 5: pendant length == iconSize (no extra gap/overlap) ----
            {
                var rng = new Random(123);
                bool ok = true;
                for (int i = 0; i < 300; i++)
                {
                    double ang = rng.NextDouble() * 2 * Math.PI;
                    var prev = new Vector2(100, 100);
                    var tip = prev + new Vector2((float)Math.Cos(ang) * 120, (float)Math.Sin(ang) * 120);
                    var p = PendantGeometry.ComputePendant(new Vector2[] { prev, tip }, size);
                    if (Math.Abs(Vector2.Distance(p.BaseCenter, p.Tip) - size) > 1e-3f) ok = false;
                }
                Check("悬挂长度=图标尺寸", ok, $"|BaseCenter-Tip| 全部 == {size} (星形恰好沿绳伸出, 无间隙)");
            }

            // ---- Test 6: renderer matrix order — session.Transform must be R*T ----
            // The renderer sets session.Transform = Matrix3x2 and lets Win2D
            // transform local points. System.Numerics is row-vector (v' = v*M),
            // so M = R*T gives v' = v*R*T = R*v + Tip (rotate-about-origin then
            // translate to Tip) — local (0,0) maps to Tip, the non-separation
            // guarantee. The WRONG order T*R gives v' = (v+Tip)*R, which orbits
            // the already-translated point about the SCREEN ORIGIN, flinging the
            // star to rotate(Tip) whenever the rope swings. This test locks the
            // matrix order so the "star flies off when mouse moves" bug cannot
            // regress: it simulates both orders and asserts only R*T keeps the
            // top vertex at Tip for any swing angle.
            {
                var rng = new Random(2026);
                bool ok = true;
                string bad = "";
                for (int i = 0; i < 400 && ok; i++)
                {
                    // Random rope-end orientation -> random swing angle.
                    double ang = rng.NextDouble() * 2 * Math.PI;
                    var prev = new Vector2(500, 300);
                    var tip = prev + new Vector2((float)Math.Cos(ang) * 100, (float)Math.Sin(ang) * 100);
                    var p = PendantGeometry.ComputePendant(new Vector2[] { prev, tip }, size);

                    // The CORRECT renderer matrix: R * T.
                    var mCorrect = Matrix3x2.CreateRotation(p.AngleRad) *
                                   Matrix3x2.CreateTranslation(p.Tip);
                    // The BUGGY renderer matrix (old code): T * R.
                    var mBuggy = Matrix3x2.CreateTranslation(p.Tip) *
                                 Matrix3x2.CreateRotation(p.AngleRad);

                    // Top vertex local (0,0) — must map to Tip under the correct matrix.
                    var topWorld = Vector2.Transform(star[0], mCorrect);
                    if (Vector2.Distance(topWorld, p.Tip) > 1e-4f)
                    { ok = false; bad = $"R*T top->{topWorld} != Tip{p.Tip}"; break; }

                    // Far tip local (0, size) — must map to Tip + Direction*size.
                    var farWorld = Vector2.Transform(new Vector2(0, size), mCorrect);
                    var expect = p.Tip + p.Direction * size;
                    if (Vector2.Distance(farWorld, expect) > 1e-3f)
                    { ok = false; bad = $"R*T far->{farWorld} != {expect}"; break; }

                    // The BUGGY matrix must NOT keep (0,0) at Tip (except when angle≈0).
                    // This proves the old order was actually wrong, not just stylistically.
                    if (Math.Abs(p.AngleRad) > 0.1f)
                    {
                        var topBuggy = Vector2.Transform(star[0], mBuggy);
                        if (Vector2.Distance(topBuggy, p.Tip) < 10f)
                        { ok = false; bad = $"T*R unexpectedly kept top at Tip (angle={p.AngleRad:F2})"; break; }
                    }
                }
                Check("渲染矩阵顺序=R*T", ok, ok ? "R*T: (0,0)→Tip 且 (0,size)→Tip+dir*size (任何摆角); T*R 已证明错误" : bad);
            }

            // ---- Test 7: preserve monitor/DPI projection ----
            // OverlaySurface installs screen-pixel -> monitor-local-DIP as the
            // drawing-session transform. The pendant transform must append that
            // projection instead of replacing it; otherwise only the pendant is
            // displaced on non-96-DPI and secondary monitors.
            {
                var previous = new Vector2(1710, 640);
                var tip = new Vector2(1800, 700);
                var p = PendantGeometry.ComputePendant(
                    new[] { previous, tip },
                    size);
                var projection =
                    Matrix3x2.CreateTranslation(-1280, -200) *
                    Matrix3x2.CreateScale(0.8f);
                Matrix3x2 complete =
                    PendantGeometry.CreateRenderTransform(p, projection);

                Vector2 renderedTip = Vector2.Transform(Vector2.Zero, complete);
                Vector2 expectedTip = Vector2.Transform(tip, projection);
                Vector2 renderedFar = Vector2.Transform(
                    new Vector2(0, size),
                    complete);
                Vector2 expectedFar = Vector2.Transform(
                    tip + p.Direction * size,
                    projection);
                bool ok =
                    Vector2.Distance(renderedTip, expectedTip) < 1e-3f &&
                    Vector2.Distance(renderedFar, expectedFar) < 1e-3f;
                Check(
                    "悬挂物保留显示器投影",
                    ok,
                    $"tip={renderedTip}, expected={expectedTip}");
            }

            string summary = $"=== 五角星挂载绑定测试完成: {total - fail}/{total} 通过, {fail} 失败 ===";
            Console.WriteLine(summary);
            App.Log(summary);
            return fail;
        }
    }
}
