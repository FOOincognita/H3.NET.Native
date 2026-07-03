// SPDX-License-Identifier: Apache-2.0

using CsCheck;
using Xunit;

namespace H3.NET.Native.Tests.Properties;

/// <summary>
/// Grid-disk invariants: the disk size never exceeds the maximum 3k(k+1)+1, the origin
/// is always present, and every returned cell is valid.
/// </summary>
public sealed class GridDiskPropertyTests
{
    private const long Iterations = 150;

    [Fact]
    public void GridDisk_RespectsMaxSize_IncludesOrigin_AndYieldsValidCells()
    {
        // Keep k modest for CI speed; max size grows quadratically.
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[0, 4], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);
                var disk = origin.GridDisk(k);

                int maxSize = (3 * k * (k + 1)) + 1;
                Assert.True(disk.Length <= maxSize);
                Assert.Contains(disk, c => c.Value == origin.Value);
                Assert.All(disk, c =>
                {
                    Assert.True(c.IsValidCell());
                    Assert.False(c.IsNull);
                });
            },
            iter: Iterations);
    }

    [Fact]
    public void GridDisk_K0_IsExactlyTheOrigin()
    {
        Generators.PointAtResolution.Sample(
            input =>
            {
                var (point, res) = input;
                var origin = H3Index.FromLatLng(point, res);
                var disk = origin.GridDisk(0);
                Assert.Single(disk);
                Assert.Equal(origin.Value, disk[0].Value);
            },
            iter: 100L);
    }
}
