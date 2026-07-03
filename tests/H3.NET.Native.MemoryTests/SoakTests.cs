// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace H3.NET.Native.MemoryTests;

/// <summary>
/// Managed soak tests that drive the binding's hot scalar paths, the native
/// heap-owning SUCCESS path (<see cref="H3Polygon.FromCells"/>, which allocates a
/// populated <c>LinkedGeoPolygon</c> via the SafeHandle and tears it down every
/// iteration), and the heap-owning PRE-allocation error branch (where
/// <c>validateCellSet</c> rejects an invalid cell before any native structure is
/// allocated), then assert that process and managed memory stay bounded across a
/// long run.
/// </summary>
/// <remarks>
/// This is the MANAGED leak gate: it proves bounded resident memory including the
/// native heap-ownership teardown of a POPULATED structure (exercised by the
/// success-path test) and the pre-allocation error branch. The authoritative
/// byte-level native leak gate is the pure-C valgrind harness under
/// <c>native-harness/</c> (Linux CI only); see the README in this directory.
/// </remarks>
[Trait("Category", "Soak")]
public sealed class SoakTests
{
    // Default keeps a local run to a few seconds; CI raises it via H3_SOAK_ITERS.
    private const int DefaultIterations = 200_000;

    // A well-known valid coordinate (downtown San Francisco) and resolution.
    private static readonly LatLng SampleLatLng = new(37.775938728915946, -122.41795063018799);
    private const int SampleResolution = 9;

    // A clearly invalid (non-null, non-cell) raw index used to drive the PRE-allocation
    // error branch of the heap-owning FromCells call. Non-null so it is NOT stripped
    // before the native call; invalid so upstream validateCellSet rejects it and returns
    // an error code, causing FromCells to throw before any native LinkedGeoPolygon
    // children are allocated (the head stays zeroed).
    private const ulong InvalidCellValue = 0x1UL;

    private static int Iterations
    {
        get
        {
            string? raw = Environment.GetEnvironmentVariable("H3_SOAK_ITERS");
            if (!string.IsNullOrWhiteSpace(raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                parsed > 0)
            {
                return parsed;
            }

            return DefaultIterations;
        }
    }

    [Fact]
    public void HotScalarPath_DoesNotGrowUnbounded()
    {
        int iterations = Iterations;

        // Warm up the JIT, the native lib, and the GC heap so the baseline reflects
        // steady state rather than first-call allocations.
        Action body = RunScalarIteration;
        Warmup(body);

        (long managedBaseline, long rssBaseline) = CaptureBaseline();

        long managedPeak = managedBaseline;
        long rssPeak = rssBaseline;
        int sampleInterval = Math.Max(1, iterations / 50);

        for (int i = 0; i < iterations; i++)
        {
            body();

            if (i % sampleInterval == 0)
            {
                (long managed, long rss) = Sample();
                managedPeak = Math.Max(managedPeak, managed);
                rssPeak = Math.Max(rssPeak, rss);
            }
        }

        AssertBounded(iterations, managedBaseline, managedPeak, rssBaseline, rssPeak);
    }

    [Fact]
    public void HeapOwningPath_FromCells_DoesNotGrowUnbounded()
    {
        int iterations = Iterations;

        // A small, stable cell set: the sample cell plus its k=1 ring. FromCells over
        // this exercises cellsToLinkedMultiPolygon + DestroyLinkedMultiPolygon +
        // the SafeHandle free on the SUCCESS path, every iteration.
        var origin = H3Index.FromLatLng(SampleLatLng, SampleResolution);
        var cells = origin.GridDisk(1);

        Action body = () =>
        {
            IReadOnlyList<GeoPolygon> polygons = H3Polygon.FromCells(cells);
            // Touch the result so the call is not optimized away.
            Assert.NotEmpty(polygons);
        };

        Warmup(body);

        (long managedBaseline, long rssBaseline) = CaptureBaseline();

        long managedPeak = managedBaseline;
        long rssPeak = rssBaseline;
        int sampleInterval = Math.Max(1, iterations / 50);

        for (int i = 0; i < iterations; i++)
        {
            body();

            if (i % sampleInterval == 0)
            {
                (long managed, long rss) = Sample();
                managedPeak = Math.Max(managedPeak, managed);
                rssPeak = Math.Max(rssPeak, rss);
            }
        }

        AssertBounded(iterations, managedBaseline, managedPeak, rssBaseline, rssPeak);
    }

    [Fact]
    public void HeapOwningPath_ExceptionPath_AlwaysThrowsTyped_DoesNotGrowUnbounded()
    {
        int iterations = Iterations;

        // Drive the PRE-allocation error branch of the heap-owning code: upstream
        // validateCellSet rejects the invalid (non-null) cell BEFORE any native
        // LinkedGeoPolygon children are allocated, so FromCells throws while the
        // SafeHandle's head is still zeroed and its release runs as a no-op. This
        // covers the validateCellSet failure branch, asserting it NEVER segfaults and
        // ALWAYS throws a typed H3Exception subtype across a long run.
        //
        // Teardown of a POPULATED LinkedGeoPolygon is NOT exercised here (the head is
        // never populated). That path is covered every iteration by the success-path
        // soak test above (HeapOwningPath_FromCells_DoesNotGrowUnbounded disposes a
        // populated handle) and, at byte-level, by the pure-C valgrind harness in
        // native-harness/leakcheck.c.
        H3Index[] invalid = [new H3Index(InvalidCellValue)];

        Action body = () =>
        {
            Assert.ThrowsAny<H3Exception>(() => H3Polygon.FromCells(invalid));
        };

        Warmup(body);

        (long managedBaseline, long rssBaseline) = CaptureBaseline();

        long managedPeak = managedBaseline;
        long rssPeak = rssBaseline;
        int sampleInterval = Math.Max(1, iterations / 50);

        for (int i = 0; i < iterations; i++)
        {
            body();

            if (i % sampleInterval == 0)
            {
                (long managed, long rss) = Sample();
                managedPeak = Math.Max(managedPeak, managed);
                rssPeak = Math.Max(rssPeak, rss);
            }
        }

        AssertBounded(iterations, managedBaseline, managedPeak, rssBaseline, rssPeak);
    }

    private static void RunScalarIteration()
    {
        var cell = H3Index.FromLatLng(SampleLatLng, SampleResolution);
        var center = cell.ToLatLng();
        _ = center.LatitudeDegrees;
        IReadOnlyList<LatLng> boundary = cell.GetBoundary();
        _ = boundary.Count;
        H3Index[] disk = cell.GridDisk(2);
        _ = disk.Length;
    }

    // Runs a representative number of iterations to reach steady state before the
    // baseline is captured, so first-call/JIT/native-init allocations are excluded.
    private static void Warmup(Action body)
    {
        const int WarmupIterations = 5_000;
        for (int i = 0; i < WarmupIterations; i++)
        {
            body();
        }
    }

    private static (long Managed, long Rss) CaptureBaseline()
    {
        // GetTotalMemory(forceFullCollection: true) settles the managed heap; refresh
        // the process to get a current working-set reading.
        long managed = GC.GetTotalMemory(forceFullCollection: true);
        long rss = CurrentWorkingSet();
        return (managed, rss);
    }

    private static (long Managed, long Rss) Sample()
    {
        long managed = GC.GetTotalMemory(forceFullCollection: true);
        long rss = CurrentWorkingSet();
        return (managed, rss);
    }

    private static long CurrentWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    // Asserts bounded growth with headroom generous enough to absorb GC scheduling,
    // working-set churn, and allocator fragmentation, while still catching a genuine
    // monotonic (per-iteration) leak, which over hundreds of thousands of iterations
    // would dwarf any fixed allowance.
    private static void AssertBounded(
        long iterations,
        long managedBaseline,
        long managedPeak,
        long rssBaseline,
        long rssPeak)
    {
        // Managed: a real per-iteration managed leak grows without bound; allow a
        // multiplicative slack over the post-warmup baseline plus a fixed floor for
        // tiny baselines. GC.GetTotalMemory(true) already discounts collectible
        // garbage, so a stable run keeps peak near baseline.
        const double ManagedSlackFactor = 2.0;       // up to 2x the settled baseline
        const long ManagedFixedHeadroomBytes = 16L * 1024 * 1024; // + 16 MB floor
        long managedLimit = (long)(managedBaseline * ManagedSlackFactor) + ManagedFixedHeadroomBytes;

        Assert.True(
            managedPeak <= managedLimit,
            FormatGrowthMessage("Managed heap", managedBaseline, managedPeak, managedLimit));

        // Under AddressSanitizer the process RSS is legitimately inflated by shadow
        // memory, per-allocation redzones, and the freed-memory quarantine, so the
        // RSS heuristic below false-positives. The asan-linux CI job disables it via
        // H3_SOAK_CHECK_RSS=0 because LSan itself is the stronger leak oracle there;
        // the managed-heap bound above stays active everywhere.
        if (string.Equals(
                Environment.GetEnvironmentVariable("H3_SOAK_CHECK_RSS"),
                "0",
                StringComparison.Ordinal))
        {
            return;
        }

        // RSS is the noisiest signal (OS lazily reclaims, allocator caches, other
        // threads), but a flat floor does not scale with run length: a small per-
        // iteration native leak can stay under a generous fixed floor at the default
        // iteration count and pass. So the allowance is built from two parts:
        //   1. A fixed NOISE floor that does NOT scale with iterations (allocator
        //      caches/working-set churn are roughly constant), plus a small
        //      multiplicative slack over the settled baseline.
        //   2. A per-iteration LEAK budget that scales with the run, capped at a sane
        //      maximum. Anything that commits more than ~RssPerIterationBudgetBytes
        //      per iteration (a genuine native leak keeps committing pages every
        //      iteration) blows past this term; benign churn does not.
        // The cap keeps CI's high iteration counts (~5,000,000) from inflating the
        // bound into uselessness while still admitting benign churn; a real leak of a
        // LinkedGeoPolygon (hundreds of bytes to KB per iteration) exceeds the budget
        // long before the cap is reached. The byte-level authoritative gate remains the
        // pure-C valgrind harness under native-harness/.
        const double RssSlackFactor = 1.25;                       // mild slack over baseline
        const long RssNoiseFloorBytes = 96L * 1024 * 1024;        // constant churn allowance
        const long RssPerIterationBudgetBytes = 64;               // conservative leak budget/iter
        const long RssLeakBudgetCapBytes = 256L * 1024 * 1024;    // cap the scaling term

        long scaledLeakBudget = Math.Min(
            checked(iterations * RssPerIterationBudgetBytes),
            RssLeakBudgetCapBytes);
        long rssLimit =
            (long)(rssBaseline * RssSlackFactor) + RssNoiseFloorBytes + scaledLeakBudget;

        Assert.True(
            rssPeak <= rssLimit,
            FormatGrowthMessage("Working set (RSS)", rssBaseline, rssPeak, rssLimit));
    }

    private static string FormatGrowthMessage(string label, long baseline, long peak, long limit) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} grew beyond the allowed bound: baseline={1:N0} B, peak={2:N0} B, limit={3:N0} B. " +
            "This indicates unbounded growth (possible leak) rather than GC/allocator noise.",
            label,
            baseline,
            peak,
            limit);
}
