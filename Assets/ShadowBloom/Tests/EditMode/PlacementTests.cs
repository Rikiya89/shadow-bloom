using NUnit.Framework;
using UnityEngine;

namespace ShadowBloom.Tests
{
    public class PlacementTests
    {
        static readonly Vector3 SeatedHead = new Vector3(0f, 1.2f, 0f);

        static SurfaceCandidate Table(Vector3 center, float yawDeg, Vector2 size)
        {
            var rot = Quaternion.Euler(0f, yawDeg, 0f);
            return new SurfaceCandidate(center, Vector3.up, rot * Vector3.right, rot * Vector3.forward, size, SurfaceKind.Table);
        }

        static Vector2 LocalOf(in SurfaceCandidate s, Vector3 p)
        {
            var d = p - s.Center;
            return new Vector2(Vector3.Dot(d, s.AxisU), Vector3.Dot(d, s.AxisV));
        }

        [Test]
        public void Placement_StaysInsideSurface_AndFacesAwayFromPlayer()
        {
            var table = Table(new Vector3(0.1f, 0.72f, 0.9f), 23f, new Vector2(1.2f, 0.7f));
            var p = SurfacePlacementSolver.Solve(table, SeatedHead, Vector3.forward);
            var local = LocalOf(table, p.Position);

            Assert.That(Mathf.Abs(local.x), Is.LessThanOrEqualTo(table.Size.x * 0.5f - p.PuzzleRadius));
            Assert.That(Mathf.Abs(local.y), Is.LessThanOrEqualTo(table.Size.y * 0.5f - p.PuzzleRadius));
            Assert.That(Vector3.Dot(p.Rotation * Vector3.up, Vector3.up), Is.GreaterThan(0.999f));

            var toPuzzle = p.Position - SeatedHead;
            toPuzzle.y = 0f;
            Assert.That(Vector3.Dot(p.Rotation * Vector3.forward, toPuzzle.normalized), Is.GreaterThan(0.99f));
        }

        // Size, yaw, centre: a deep desk, a narrow shelf-like desk, a small side table, a turned table, a tiny stool.
        [TestCase(1.2f, 0.7f, 23f, 0.1f, 0.9f)]
        [TestCase(1.6f, 0.45f, 0f, 0f, 0.55f)]
        [TestCase(0.5f, 0.5f, 40f, 0f, 0.5f)]
        [TestCase(0.9f, 0.6f, 75f, -0.2f, 0.6f)]
        [TestCase(0.28f, 0.28f, 10f, 0f, 0.45f)]
        public void GardenBand_GrowsWithFreeTable_AndEveryFlowerStaysOnIt(float w, float d, float yaw, float cx, float cz)
        {
            var table = Table(new Vector3(cx, 0.72f, cz), yaw, new Vector2(w, d));
            var p = SurfacePlacementSolver.Solve(table, SeatedHead, Vector3.forward);
            Assert.That(p.GardenBand, Is.InRange(GardenLayout.MinBand, GardenLayout.MaxBand));
            TestContext.WriteLine($"{w} x {d} m table: radius {p.PuzzleRadius:F3}, garden band {p.GardenBand:F3}");

            bool roomForTheShadow = Mathf.Min(w, d) * 0.5f > p.PuzzleRadius + GardenLayout.MinBand;
            if (!roomForTheShadow) Assert.That(p.GardenBand, Is.EqualTo(GardenLayout.MinBand), "a tiny surface keeps the small garden");

            float extent = GardenLayout.Extent * GardenLayout.Scale(p.GardenBand);
            for (int slot = 0; slot < 7 && roomForTheShadow; slot++)
            {
                var local = GardenLayout.SlotLocal(slot, p.PuzzleRadius, p.GardenBand);
                var flat = new Vector3(local.x, 0f, local.z);
                var outer = p.Position + p.Rotation * (flat + flat.normalized * extent);
                var onTable = LocalOf(table, outer);
                Assert.That(Mathf.Abs(onTable.x), Is.LessThanOrEqualTo(w * 0.5f + 1e-4f), $"slot {slot} on the table (u)");
                Assert.That(Mathf.Abs(onTable.y), Is.LessThanOrEqualTo(d * 0.5f + 1e-4f), $"slot {slot} on the table (v)");
            }
        }

        [Test]
        public void GardenBand_IsLargest_OnADeepDesk_AndOnTheNoScanFallback()
        {
            var desk = Table(new Vector3(0f, 0.72f, 0.7f), 0f, new Vector2(1.4f, 0.8f));
            Assert.That(SurfacePlacementSolver.Solve(desk, SeatedHead, Vector3.forward).GardenBand, Is.EqualTo(GardenLayout.MaxBand));
            var fallback = SurfacePlacementSolver.HeadRelativeFallback(SeatedHead, Vector3.forward);
            Assert.That(SurfacePlacementSolver.Solve(fallback, SeatedHead, Vector3.forward).GardenBand, Is.EqualTo(GardenLayout.MaxBand));
        }

        [Test]
        public void HeadReady_WaitsForATrackedPose_WhileTheXrRuntimeStarts()
        {
            // Quest 2, 1.9 s after launch: runtime loaded, head still at the floor-level rig origin. Placing then put the
            // table 0.5 m under the floor (device log 2026-10-31).
            Assert.IsNull(SurfacePlacementController.HeadReadyReason(true, 0f, 0.5f, 1.9f, 5f));
            Assert.AreEqual("tracked", SurfacePlacementController.HeadReadyReason(true, 0.96f, 0.5f, 2.5f, 5f));
            // Eye-level origin: the head never rises above the threshold, so the wait times out instead of stalling.
            Assert.AreEqual("timeout", SurfacePlacementController.HeadReadyReason(true, 0f, 0.5f, 5.1f, 5f));
            // Desktop debug path: no runtime will ever deliver a pose, place at once.
            Assert.AreEqual("no XR runtime", SurfacePlacementController.HeadReadyReason(false, 0f, 0.5f, 0f, 5f));
        }

        [Test]
        public void Placement_PrefersNearEdge_OfLargeTable()
        {
            var table = Table(new Vector3(0f, 0.72f, 1.2f), 0f, new Vector2(1.6f, 1.6f));
            var p = SurfacePlacementSolver.Solve(table, SeatedHead, Vector3.forward);
            Assert.That(p.Position.z, Is.LessThan(table.Center.z), "puzzle should be on the player's side");
            var flat = p.Position - SeatedHead;
            flat.y = 0f;
            Assert.That(flat.magnitude, Is.LessThan(0.9f), "within seated reach");
        }

        [Test]
        public void Radius_ScalesWithSurface_AndIsClamped()
        {
            Assert.That(SurfacePlacementSolver.RadiusFor(new Vector2(0.25f, 0.25f)), Is.EqualTo(SurfacePlacementSolver.MinRadius));
            Assert.That(SurfacePlacementSolver.RadiusFor(new Vector2(3f, 3f)), Is.EqualTo(SurfacePlacementSolver.MaxRadius));
            float mid = SurfacePlacementSolver.RadiusFor(new Vector2(0.4f, 0.9f));
            Assert.That(mid, Is.EqualTo(0.16f).Within(1e-4f));
        }

        [Test]
        public void LowTable_LiftsPuzzleToComfortHeight_NotDownAtTheTable()
        {
            var low = Table(new Vector3(0f, 0.45f, 0.6f), 0f, new Vector2(1f, 0.6f));  // coffee table, 0.75 m below the eyes
            var p = SurfacePlacementSolver.Solve(low, SeatedHead, Vector3.forward);
            float centreBelowEyes = SeatedHead.y - (p.Position.y + p.PivotHeight);
            Assert.That(centreBelowEyes, Is.EqualTo(SurfacePlacementSolver.EyeToPuzzleDrop).Within(1e-3f));
        }

        [Test]
        public void HighTable_KeepsCurveClearOfSurface()
        {
            var high = Table(new Vector3(0f, 0.95f, 0.6f), 0f, new Vector2(1f, 0.6f));  // 0.25 m below the eyes
            var p = SurfacePlacementSolver.Solve(high, SeatedHead, Vector3.forward);
            Assert.That(p.PivotHeight, Is.EqualTo(SurfacePlacementSolver.PivotHeightFor(p.PuzzleRadius)).Within(1e-4f));
        }

        [Test]
        public void Fallback_UsesFullSizePuzzle_AtComfortHeight()
        {
            var fb = SurfacePlacementSolver.HeadRelativeFallback(SeatedHead, Vector3.forward);
            var p = SurfacePlacementSolver.Solve(fb, SeatedHead, Vector3.forward);
            Assert.That(p.PuzzleRadius, Is.EqualTo(SurfacePlacementSolver.MaxRadius));
            float centreBelowEyes = SeatedHead.y - (p.Position.y + p.PivotHeight);
            Assert.That(centreBelowEyes, Is.InRange(0.15f, SurfacePlacementSolver.EyeToPuzzleDrop + 1e-3f));
        }

        [Test]
        public void TinySurface_CentersPuzzle()
        {
            var table = Table(new Vector3(0f, 0.7f, 0.5f), 0f, new Vector2(0.24f, 0.24f));
            var p = SurfacePlacementSolver.Solve(table, SeatedHead, Vector3.forward);
            Assert.That((p.Position - table.Center).magnitude, Is.LessThan(0.02f), "stays near centre");
            var local = LocalOf(table, p.Position);
            Assert.That(Mathf.Abs(local.y) + p.PuzzleRadius, Is.LessThanOrEqualTo(table.Size.y * 0.5f), "shadow stays on the surface");
        }

        [Test]
        public void Score_RejectsWalls_FloorHeight_AndFarSurfaces_PrefersTables()
        {
            var wall = new SurfaceCandidate(new Vector3(0f, 1.2f, 1f), Vector3.back, Vector3.right, Vector3.up, new Vector2(2f, 2f), SurfaceKind.OtherHorizontal);
            var floor = Table(new Vector3(0f, 0f, 0.5f), 0f, new Vector2(2f, 2f));
            var far = Table(new Vector3(0f, 0.7f, 5f), 0f, new Vector2(1f, 1f));
            var table = Table(new Vector3(0f, 0.72f, 0.8f), 0f, new Vector2(1f, 0.6f));
            var shelf = table;
            shelf.Kind = SurfaceKind.OtherHorizontal;

            Assert.That(float.IsNegativeInfinity(SurfacePlacementSolver.Score(wall, SeatedHead)));
            Assert.That(float.IsNegativeInfinity(SurfacePlacementSolver.Score(floor, SeatedHead)));
            Assert.That(float.IsNegativeInfinity(SurfacePlacementSolver.Score(far, SeatedHead)));
            Assert.That(SurfacePlacementSolver.Score(table, SeatedHead), Is.GreaterThan(SurfacePlacementSolver.Score(shelf, SeatedHead)));
        }

        [Test]
        public void Fallback_IsBelowAndInFrontOfHead_EvenWhenLookingStraightDown()
        {
            var fb = SurfacePlacementSolver.HeadRelativeFallback(SeatedHead, Vector3.down);
            Assert.That(fb.Center.y, Is.LessThan(SeatedHead.y));
            Assert.That(float.IsNaN(fb.Center.x), Is.False);
            var p = SurfacePlacementSolver.Solve(fb, SeatedHead, Vector3.down);
            Assert.That(float.IsNaN(p.Position.x), Is.False);
            Assert.That(p.Kind, Is.EqualTo(SurfaceKind.Fallback));
        }

        [Test]
        public void TrySelect_RejectsTableAcrossTheRoom_AcceptsNearOne()
        {
            var far = Table(new Vector3(0f, 0.7f, 2.2f), 0f, new Vector2(1.2f, 0.8f));
            Assert.That(SurfacePlacementSolver.TrySelect(new[] { far }, SeatedHead, Vector3.forward, out _, out _), Is.False);

            var near = Table(new Vector3(0.3f, 0.72f, 0.75f), 15f, new Vector2(1f, 0.6f));
            Assert.That(SurfacePlacementSolver.TrySelect(new[] { far, near }, SeatedHead, Vector3.forward, out var p, out int index));
            Assert.That(index, Is.EqualTo(1));
            var flat = p.Position - SeatedHead;
            flat.y = 0f;
            Assert.That(flat.magnitude, Is.LessThanOrEqualTo(SurfacePlacementSolver.MaxComfortReach));
        }

        [Test]
        public void TrySelect_IgnoresSurfacesBehindPlayer_AndCouchHeight()
        {
            var behind = Table(new Vector3(0f, 0.72f, -0.6f), 0f, new Vector2(1f, 0.6f));
            var couch = Table(new Vector3(0f, 0.3f, 0.6f), 0f, new Vector2(1.6f, 0.6f));
            couch.Kind = SurfaceKind.OtherHorizontal;
            Assert.That(SurfacePlacementSolver.TrySelect(new[] { behind, couch }, SeatedHead, Vector3.forward, out _, out _), Is.False);
        }

        [Test]
        public void SlopedSurface_UsesItsOwnNormal()
        {
            var n = Quaternion.Euler(10f, 0f, 0f) * Vector3.up;
            var s = new SurfaceCandidate(new Vector3(0f, 0.7f, 0.7f), n, Vector3.right, Vector3.Cross(Vector3.right, n), new Vector2(0.8f, 0.6f), SurfaceKind.Table);
            var p = SurfacePlacementSolver.Solve(s, SeatedHead, Vector3.forward);
            Assert.That(Vector3.Dot(p.Rotation * Vector3.up, n), Is.GreaterThan(0.999f));
        }
    }
}
