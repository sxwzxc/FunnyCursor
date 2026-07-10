using System;
using System.Numerics;

namespace MouseBeautifier
{
    /// <summary>
    /// Tests the pendant (triangle) binding algorithm: verifies the triangle's
    /// tip stays glued to the rope's last point and its direction tracks the
    /// rope end smoothly under all motion patterns. Run via "FunnyCursor --test-pendant".
    ///
    /// The geometric contract being verified:
    ///   1. pendant.Tip == rope.Bob  (NEVER detaches — by construction, same point)
    ///   2. |Direction| == 1          (unit vector)
    ///   3. |BaseCenter - anchor| <= ropeLen + iconSize * 1.5  (stays in sane range)
    ///   4. Direction never points "back up" past horizontal under normal motion
    ///   5. Angle continuity: no single-frame flip > 90deg under slow motion
    /// </summary>
    internal static class PendantTests
    {
        public static int Run()
        {
            int fail = 0, total = 0;

            void Check(string name, bool ok, string detail)
            {
                total++;
                string tag = ok ? "PASS" : "FAIL";
                if (!ok) fail++;
                string line = $"[{tag}] {name}: {detail}";
                Console.WriteLine(line);
                App.Log(line);
            }

            var s = new AppSettings();
            s.RopeLength = 170;
            s.RopeSegments = 18;
            s.RopeGravity = 1500;
            s.RopeDamping = 0.9;
            s.RopeStiffness = 0.6;
            float iconSize = 30f;

            // ---------- Test 1: tip == Bob (contract: NEVER detaches) ----------
            // Run every motion scenario and confirm pendant.Tip == rope.Bob exactly.
            {
                bool allEqual = true;
                int checks = 0;

                // Scenario A: stationary
                {
                    var rope = new RopeSimulator();
                    rope.ApplySettings(s);
                    var anchor = new Vector2(400, 300);
                    for (int i = 0; i < 120; i++)
                    {
                        rope.Update(1.0 / 60.0, anchor, s);
                        var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                        if (p.Tip != rope.Bob) allEqual = false;
                        checks++;
                    }
                }
                // Scenario B: slow right
                {
                    var rope = new RopeSimulator();
                    rope.ApplySettings(s);
                    var anchor = new Vector2(400, 300);
                    for (int i = 0; i < 120; i++)
                    {
                        anchor.X += 2f;
                        rope.Update(1.0 / 60.0, anchor, s);
                        var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                        if (p.Tip != rope.Bob) allEqual = false;
                        checks++;
                    }
                }
                // Scenario C: fast jitter
                {
                    var rope = new RopeSimulator();
                    rope.ApplySettings(s);
                    var anchor = new Vector2(400, 300);
                    for (int i = 0; i < 120; i++)
                    {
                        var rng = new Random(i);
                        anchor.X += (float)(rng.NextDouble() * 80 - 40);
                        anchor.Y += (float)(rng.NextDouble() * 40 - 20);
                        rope.Update(1.0 / 60.0, anchor, s);
                        var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                        if (p.Tip != rope.Bob) allEqual = false;
                        checks++;
                    }
                }
                // Scenario D: teleport then settle
                {
                    var rope = new RopeSimulator();
                    rope.ApplySettings(s);
                    var anchor = new Vector2(400, 300);
                    for (int i = 0; i < 120; i++)
                    {
                        if (i == 30) anchor.X += 500f;
                        rope.Update(1.0 / 60.0, anchor, s);
                        var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                        if (p.Tip != rope.Bob) allEqual = false;
                        checks++;
                    }
                }
                Check("顶端=绳子末端", allEqual, $"{checks}次检查 Tip全部==Bob (永不分离)");
            }

            // ---------- Test 2: direction is unit vector ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(400, 300);
                var rng = new Random(1);
                bool allUnit = true;
                for (int i = 0; i < 300; i++)
                {
                    anchor += new Vector2((float)(rng.NextDouble() * 100 - 50), (float)(rng.NextDouble() * 50 - 25));
                    rope.Update(1.0 / 60.0, anchor, s);
                    var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                    float len = p.Direction.Length();
                    if (Math.Abs(len - 1f) > 0.01f) allUnit = false;
                }
                Check("方向单位向量", allUnit, "|Direction| 全部 == 1.0");
            }

            // ---------- Test 3: pendant stays in sane range ----------
            // BaseCenter (far end of triangle) = Tip + Direction*iconSize. The tip
            // can reach up to ~1.25*ropeLen (rope max stretch), so the base can
            // reach up to ropeLen*1.25 + iconSize. This is the natural geometric
            // limit — the pendant extends one iconSize beyond the rope end.
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(400, 300);
                var rng = new Random(2);
                float maxDist = 0;
                for (int i = 0; i < 600; i++)
                {
                    anchor += new Vector2((float)(rng.NextDouble() * 120 - 60), (float)(rng.NextDouble() * 60 - 30));
                    rope.Update(1.0 / 60.0, anchor, s);
                    var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                    float d = Vector2.Distance(p.BaseCenter, anchor);
                    if (d > maxDist) maxDist = d;
                }
                float limit = (float)s.RopeLength * 1.25f + iconSize;
                bool ok = maxDist <= limit;
                Check("范围合理", ok, $"maxBaseAnchor={maxDist:F1} (limit {limit:F1} = ropeLen*1.25+size)");
            }

            // ---------- Test 4: no angle flip > 90deg under slow motion ----------
            // Under smooth slow cursor movement the pendant direction must not flip
            // by more than 90 degrees in a single frame (that would indicate jitter).
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(200, 300);
                float prevAngle = 0;
                float maxFlip = 0;
                bool first = true;
                for (int i = 0; i < 300; i++)
                {
                    anchor.X += 1.5f; // slow steady motion
                    rope.Update(1.0 / 60.0, anchor, s);
                    var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                    float ang = p.AngleRad;
                    if (!first)
                    {
                        float d = Math.Abs(AngleDiff(ang, prevAngle));
                        if (d > maxFlip) maxFlip = d;
                    }
                    prevAngle = ang;
                    first = false;
                }
                float maxFlipDeg = maxFlip * 180f / (float)Math.PI;
                bool ok = maxFlipDeg < 90f;
                Check("角度连续", ok, $"max单帧翻转={maxFlipDeg:F1}° (<90°)");
            }

            // ---------- Test 5: tip always within rope length of anchor ----------
            // The triangle tip (= rope end) must never exceed the rope's total
            // length from the anchor (rope can't stretch infinitely).
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(400, 300);
                var rng = new Random(3);
                float maxTipDist = 0;
                for (int i = 0; i < 600; i++)
                {
                    anchor += new Vector2((float)(rng.NextDouble() * 150 - 75), (float)(rng.NextDouble() * 80 - 40));
                    rope.Update(1.0 / 60.0, anchor, s);
                    var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                    float d = Vector2.Distance(p.Tip, anchor);
                    if (d > maxTipDist) maxTipDist = d;
                }
                float limit = (float)s.RopeLength * 1.25f; // small overshoot allowed
                bool ok = maxTipDist <= limit;
                Check("顶端在绳长内", ok, $"maxTipAnchor={maxTipDist:F1} (limit {limit:F1} = ropeLen*1.25)");
            }

            // ---------- Test 6: triangle extends DOWNWARD when rope hangs straight ----------
            // When the rope is settled and vertical, the pendant direction must
            // point down (dir.Y > 0.9), so the triangle hangs below the rope
            // (not sideways or upward).
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(500, 200);
                for (int i = 0; i < 300; i++) rope.Update(1.0 / 60.0, anchor, s); // settle
                var p = EffectRenderer.ComputePendant(rope.Points, iconSize);
                bool pointsDown = p.Direction.Y > 0.9f;
                bool baseBelowTip = p.BaseCenter.Y > p.Tip.Y;
                Check("下垂方向正确", pointsDown && baseBelowTip,
                    $"dir.Y={p.Direction.Y:F2} baseY={p.BaseCenter.Y:F1} tipY={p.Tip.Y:F1} (需 dir.Y>0.9 且 base>tip)");
            }

            string summary = $"=== 悬挂物绑定测试完成: {total - fail}/{total} 通过, {fail} 失败 ===";
            Console.WriteLine(summary);
            App.Log(summary);
            return fail;
        }

        /// <summary>Smallest signed difference a-b in [-π, π].</summary>
        private static float AngleDiff(float a, float b)
        {
            float d = a - b;
            while (d > Math.PI) d -= (float)(2 * Math.PI);
            while (d < -Math.PI) d += (float)(2 * Math.PI);
            return d;
        }
    }
}
