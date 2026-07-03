// SPDX-License-Identifier: Apache-2.0

using CsCheck;
using Xunit;

namespace H3.NET.Native.Tests.Properties;

/// <summary>
/// Round-trip invariants: re-binning a cell's own center at the same resolution must
/// return the same cell (FromLatLng -> ToLatLng -> FromLatLng is a fixed point).
/// </summary>
public sealed class RoundTripPropertyTests
{
    private const long Iterations = 200;

    [Fact]
    public void CellCenter_ReBins_ToSameCell()
    {
        Generators.PointAtResolution.Sample(
            input =>
            {
                var (point, res) = input;
                var cell = H3Index.FromLatLng(point, res);

                // The cell's center must map back to the same cell.
                var center = cell.ToLatLng();
                var reBinned = H3Index.FromLatLng(center, res);

                Assert.Equal(cell.Value, reBinned.Value);
            },
            iter: Iterations);
    }

    [Fact]
    public void EveryProducedCell_IsValid_WithRequestedResolution()
    {
        Generators.PointAtResolution.Sample(
            input =>
            {
                var (point, res) = input;
                var cell = H3Index.FromLatLng(point, res);
                Assert.True(cell.IsValidCell());
                Assert.Equal(res, cell.Resolution);
                Assert.False(cell.IsNull);
            },
            iter: Iterations);
    }
}
