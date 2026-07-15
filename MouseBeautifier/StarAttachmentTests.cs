using System;
using System.Numerics;

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

            string summary = $"=== 五角星挂载绑定测试完成: {total - fail}/{total} 通过, {fail} 失败 ===";
            Console.WriteLine(summary);
            App.Log(summary);
            return fail;
        }
    }
}
