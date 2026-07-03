// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace H3.NET.Native.Interop;

// Hand-written P/Invoke surface, 1:1 with the H3 v4.5.0 C ABI.
//
// - [LibraryImport("h3")] resolves to libh3.dylib (macOS) / libh3.so (linux),
//   copied next to consumer output by the repo's native-copy targets.
// - H3 exports BARE C names; EntryPoint pins the exact symbol while the C#
//   method name stays PascalCase to satisfy the repo analyzer rules.
// - H3Index = ulong, H3Error = uint (modeled as H3ErrorCode).
// - Inspection helpers return a bare C int (NOT H3Error); callers must validate
//   inputs first because those entry points do not report errors.
// - No [SuppressGCTransition] anywhere (deferred per design).
internal static unsafe partial class NativeMethods
{
    // ---- Core indexing -----------------------------------------------------

    [LibraryImport("h3", EntryPoint = "latLngToCell")]
    internal static partial H3ErrorCode LatLngToCell(in NativeLatLng g, int res, out ulong outCell);

    [LibraryImport("h3", EntryPoint = "cellToLatLng")]
    internal static partial H3ErrorCode CellToLatLng(ulong cell, out NativeLatLng g);

    [LibraryImport("h3", EntryPoint = "cellToBoundary")]
    internal static partial H3ErrorCode CellToBoundary(ulong cell, out CellBoundary boundary);

    // ---- Grid traversal ----------------------------------------------------

    [LibraryImport("h3", EntryPoint = "maxGridDiskSize")]
    internal static partial H3ErrorCode MaxGridDiskSize(int k, out long size);

    // out buffer length must be >= maxGridDiskSize(k); unused slots are H3_NULL.
    [LibraryImport("h3", EntryPoint = "gridDisk")]
    internal static partial H3ErrorCode GridDisk(ulong origin, int k, ulong* outCells);

    // Parallel to maxGridDiskSize: gives 6*k cells for the hollow ring (1 at k=0).
    [LibraryImport("h3", EntryPoint = "maxGridRingSize")]
    internal static partial H3ErrorCode MaxGridRingSize(int k, out long size);

    // Pentagon-SAFE wrapper: self-dispatches to gridRingUnsafe and falls back on
    // distortion. out length must be >= maxGridRingSize(k); on pentagon holes some
    // slots stay H3_NULL, so callers strip defensively. Order is not guaranteed.
    [LibraryImport("h3", EntryPoint = "gridRing")]
    internal static partial H3ErrorCode GridRing(ulong origin, int k, ulong* outCells);

    // Size-half of the gridPathCells M4 pair. EXACT length (gridDistance + 1);
    // propagates E_FAILED for far-apart / mismatched-resolution endpoints.
    [LibraryImport("h3", EntryPoint = "gridPathCellsSize")]
    internal static partial H3ErrorCode GridPathCellsSize(ulong start, ulong end, out long size);

    // out length must be == gridPathCellsSize(start, end). Endpoint-inclusive:
    // out[0] == start, out[^1] == end. No H3_NULL padding (exact size).
    [LibraryImport("h3", EntryPoint = "gridPathCells")]
    internal static partial H3ErrorCode GridPathCells(ulong start, ulong end, ulong* outCells);

    [LibraryImport("h3", EntryPoint = "gridDistance")]
    internal static partial H3ErrorCode GridDistance(ulong origin, ulong h3, out long distance);

    // SAFE-dispatching wrapper. distances is int* (NOT int64). Both buffers sized to
    // maxGridDiskSize(k): the cells buffer is the H3_NULL sentinel channel; distances
    // carries no sentinel (origin's distance is legitimately 0).
    [LibraryImport("h3", EntryPoint = "gridDiskDistances")]
    internal static partial H3ErrorCode GridDiskDistances(ulong origin, int k, ulong* outCells, int* distances);

    // mode is reserved (only 0 is defined); the public layer hides it and always passes 0.
    [LibraryImport("h3", EntryPoint = "cellToLocalIj")]
    internal static partial H3ErrorCode CellToLocalIj(ulong origin, ulong h3, uint mode, out NativeCoordIJ outIj);

    // const CoordIJ* ij -> `in` blittable struct marshals as a pointer. mode reserved (pass 0).
    [LibraryImport("h3", EntryPoint = "localIjToCell")]
    internal static partial H3ErrorCode LocalIjToCell(ulong origin, in NativeCoordIJ ij, uint mode, out ulong outCell);

    // ---- Region (polygon) operations ---------------------------------------

    [LibraryImport("h3", EntryPoint = "maxPolygonToCellsSize")]
    internal static partial H3ErrorCode MaxPolygonToCellsSize(
        NativeGeoPolygon* geoPolygon, int res, uint flags, out long size);

    // Non-experimental polygonToCells always uses CONTAINMENT_CENTER and ignores the
    // other containment modes, so callers pass flags == 0 (the only value with well-
    // defined behavior). The validator only rejects bits outside the containment-mode
    // mask (or a mode >= CONTAINMENT_INVALID) with OptionInvalid; modes 1-3 are
    // accepted but ignored. out buffer length must be >= maxPolygonToCellsSize; unused
    // slots are H3_NULL.
    [LibraryImport("h3", EntryPoint = "polygonToCells")]
    internal static partial H3ErrorCode PolygonToCells(
        NativeGeoPolygon* geoPolygon, int res, uint flags, ulong* outCells);

    // Size-half of the experimental polygonToCells M4 pair. Unlike the stable sizer,
    // flags carries a real ContainmentMode value; an out-of-range mode surfaces
    // OptionInvalid. Stays INTERNAL: every official binding hides the experimental
    // sizer and computes the buffer length on the caller's behalf.
    [LibraryImport("h3", EntryPoint = "maxPolygonToCellsSizeExperimental")]
    internal static partial H3ErrorCode MaxPolygonToCellsSizeExperimental(
        NativeGeoPolygon* geoPolygon, int res, uint flags, out long size);

    // Fill-half of the experimental polygonToCells M4 pair. Mirrors the stable fill
    // but adds the explicit int64 size (caller buffer length) argument and honors the
    // ContainmentMode passed in flags. out buffer length must be == size; unused slots
    // are H3_NULL. C sig: polygonToCellsExperimental(const GeoPolygon*, int res,
    // uint32_t flags, int64_t size, H3Index* out).
    [LibraryImport("h3", EntryPoint = "polygonToCellsExperimental")]
    internal static partial H3ErrorCode PolygonToCellsExperimental(
        NativeGeoPolygon* geoPolygon, int res, uint flags, long size, ulong* outCells);

    // ---- Linked geo (multi-polygon) ----------------------------------------

    // Caller allocates and owns the head 'out' node; native fills it and heap-
    // allocates the children. Pair with DestroyLinkedMultiPolygon. See
    // LinkedGeoPolygonHandle for the verified ownership/cleanup semantics.
    [LibraryImport("h3", EntryPoint = "cellsToLinkedMultiPolygon")]
    internal static partial H3ErrorCode CellsToLinkedMultiPolygon(
        ulong* h3Set, int numHexes, NativeLinkedGeoPolygon* outPolygon);

    // Frees all loops/coords and every polygon node EXCEPT the head, which it
    // zeroes (*polygon = {0}). Idempotent. Does NOT free the head allocation.
    [LibraryImport("h3", EntryPoint = "destroyLinkedMultiPolygon")]
    internal static partial void DestroyLinkedMultiPolygon(NativeLinkedGeoPolygon* polygon);

    // ---- Error description -------------------------------------------------

    // Returns a pointer to a static C string; the public layer converts it via
    // Marshal.PtrToStringUTF8. Never freed by the caller.
    [LibraryImport("h3", EntryPoint = "describeH3Error")]
    internal static partial nint DescribeH3Error(H3ErrorCode err);

    // ---- Inspection (bare int returns; validate inputs first) --------------

    [LibraryImport("h3", EntryPoint = "getResolution")]
    internal static partial int GetResolution(ulong cell);

    [LibraryImport("h3", EntryPoint = "isValidCell")]
    internal static partial int IsValidCell(ulong cell);

    [LibraryImport("h3", EntryPoint = "isPentagon")]
    internal static partial int IsPentagon(ulong cell);

    [LibraryImport("h3", EntryPoint = "isResClassIII")]
    internal static partial int IsResClassIII(ulong cell);

    [LibraryImport("h3", EntryPoint = "getBaseCellNumber")]
    internal static partial int GetBaseCellNumber(ulong cell);

    // isValidIndex is the broader cell-OR-edge-OR-vertex validity predicate; like
    // isValidCell it never reports an error and never throws, so callers must NOT
    // validate-first against it.
    [LibraryImport("h3", EntryPoint = "isValidIndex")]
    internal static partial int IsValidIndex(ulong cell);

    // isValidDirectedEdge returns a bare C int (NOT H3Error) and never throws; it is
    // the validity predicate for a directed-edge index, so callers must NOT
    // validate-first against it.
    [LibraryImport("h3", EntryPoint = "isValidDirectedEdge")]
    internal static partial int IsValidDirectedEdge(ulong edge);

    // ---- Inspection / string conversion (H3Error channel) ------------------

    // getIndexDigit only bit-extracts the stored digit; it validates 1 <= res <= 15
    // (E_RES_DOMAIN) but never checks cell validity, so do not validate-first.
    [LibraryImport("h3", EntryPoint = "getIndexDigit")]
    internal static partial H3ErrorCode GetIndexDigit(ulong cell, int res, out int digit);

    // constructCell reads digits[0..res-1]; the caller MUST pin a span of exactly
    // res ints. Argument-domain errors: E_RES_DOMAIN, E_BASE_CELL_DOMAIN,
    // E_DIGIT_DOMAIN, E_DELETED_DIGIT.
    [LibraryImport("h3", EntryPoint = "constructCell")]
    internal static partial H3ErrorCode ConstructCell(int res, int baseCellNumber, int* digits, out ulong outCell);

    // Size-half of the getIcosahedronFaces M4 pair: 2 for hexagons, 5 for pentagons.
    [LibraryImport("h3", EntryPoint = "maxFaceCount")]
    internal static partial H3ErrorCode MaxFaceCount(ulong cell, out int count);

    // out length must be >= maxFaceCount(cell); unused slots are INVALID_FACE (-1),
    // NOT H3_NULL, because 0 is a valid icosahedron face.
    [LibraryImport("h3", EntryPoint = "getIcosahedronFaces")]
    internal static partial H3ErrorCode GetIcosahedronFaces(ulong cell, int* outFaces);

    // sscanf("%016" PRIx64): any valid 16-hex string yields the identical ulong as the
    // managed Parse fast path; an unparseable string returns E_FAILED.
    [LibraryImport("h3", EntryPoint = "stringToH3", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial H3ErrorCode StringToH3(string str, out ulong outCell);

    // sprintf("%" PRIx64): emits VARIABLE-length lowercase hex (no zero padding) into
    // the caller's buffer; requires sz >= 17 or E_MEMORY_BOUNDS.
    [LibraryImport("h3", EntryPoint = "h3ToString")]
    internal static partial H3ErrorCode H3ToString(ulong cell, byte* str, nuint sz);

    // ---- Hierarchy (parent/children/compact) -------------------------------

    [LibraryImport("h3", EntryPoint = "cellToParent")]
    internal static partial H3ErrorCode CellToParent(ulong cell, int parentRes, out ulong parent);

    [LibraryImport("h3", EntryPoint = "cellToCenterChild")]
    internal static partial H3ErrorCode CellToCenterChild(ulong cell, int childRes, out ulong child);

    [LibraryImport("h3", EntryPoint = "cellToChildPos")]
    internal static partial H3ErrorCode CellToChildPos(ulong child, int parentRes, out long pos);

    [LibraryImport("h3", EntryPoint = "childPosToCell")]
    internal static partial H3ErrorCode ChildPosToCell(long childPos, ulong parent, int childRes, out ulong child);

    // Size-half of the cellToChildren M4 pair. Exact count; pentagon parents yield
    // fewer than 7^delta children, so the tail of the fill stays H3_NULL.
    [LibraryImport("h3", EntryPoint = "cellToChildrenSize")]
    internal static partial H3ErrorCode CellToChildrenSize(ulong cell, int childRes, out long size);

    // out length must be >= cellToChildrenSize(cell, childRes); unused slots are H3_NULL.
    [LibraryImport("h3", EntryPoint = "cellToChildren")]
    internal static partial H3ErrorCode CellToChildren(ulong cell, int childRes, ulong* outChildren);

    // compactedSet must be sized to numHexes; it is filled front-to-back and the
    // remaining trailing slots stay H3_NULL.
    [LibraryImport("h3", EntryPoint = "compactCells")]
    internal static partial H3ErrorCode CompactCells(ulong* h3Set, ulong* compactedSet, long numHexes);

    // Size-half of the uncompactCells M4 pair. res must be >= the finest resolution
    // present in the set.
    [LibraryImport("h3", EntryPoint = "uncompactCellsSize")]
    internal static partial H3ErrorCode UncompactCellsSize(ulong* compactedSet, long numCompacted, int res, out long size);

    // outSet length must be >= uncompactCellsSize(set, numCompacted, res); unused
    // slots are H3_NULL.
    [LibraryImport("h3", EntryPoint = "uncompactCells")]
    internal static partial H3ErrorCode UncompactCells(ulong* compactedSet, long numCompacted, ulong* outSet, long numOut, int res);

    // ---- Directed edges ----------------------------------------------------

    // Carries a REAL H3Error channel despite the int* out param: the int reports the
    // boolean (0/1) while the return value can surface E_RES_MISMATCH for cells of
    // differing resolution. NOT a bare-int predicate.
    [LibraryImport("h3", EntryPoint = "areNeighborCells")]
    internal static partial H3ErrorCode AreNeighborCells(ulong origin, ulong destination, out int outNeighbor);

    // Throws E_NOT_NEIGHBORS when the cells are not adjacent and E_RES_MISMATCH when
    // their resolutions differ.
    [LibraryImport("h3", EntryPoint = "cellsToDirectedEdge")]
    internal static partial H3ErrorCode CellsToDirectedEdge(ulong origin, ulong destination, out ulong outEdge);

    [LibraryImport("h3", EntryPoint = "getDirectedEdgeOrigin")]
    internal static partial H3ErrorCode GetDirectedEdgeOrigin(ulong edge, out ulong outCell);

    [LibraryImport("h3", EntryPoint = "getDirectedEdgeDestination")]
    internal static partial H3ErrorCode GetDirectedEdgeDestination(ulong edge, out ulong outCell);

    // Constant-size 2 (M2): originDestination must point at exactly 2 slots; native
    // writes out[0]=origin and out[1]=destination, so there is no H3_NULL padding.
    [LibraryImport("h3", EntryPoint = "directedEdgeToCells")]
    internal static partial H3ErrorCode DirectedEdgeToCells(ulong edge, ulong* originDestination);

    // Fixed-capacity 6 (M4): edges must point at exactly 6 slots. A hexagon yields 6
    // valid edges; a pentagon yields 5 valid edges plus one H3_NULL(0) slot, so
    // callers strip the H3_NULL entries.
    [LibraryImport("h3", EntryPoint = "originToDirectedEdges")]
    internal static partial H3ErrorCode OriginToDirectedEdges(ulong origin, ulong* edges);

    [LibraryImport("h3", EntryPoint = "directedEdgeToBoundary")]
    internal static partial H3ErrorCode DirectedEdgeToBoundary(ulong edge, out CellBoundary boundary);

    [LibraryImport("h3", EntryPoint = "reverseDirectedEdge")]
    internal static partial H3ErrorCode ReverseDirectedEdge(ulong edge, out ulong outEdge);

    // ---- Vertices ----------------------------------------------------------

    // M1 scalar by-ref + H3Error. Native validates ONLY vertexNum: an out-of-range
    // vertexNum surfaces E_DOMAIN (so callers must NOT pre-clamp it). It does NOT
    // validate the origin cell (no E_CELL_INVALID): an invalid origin may return
    // E_SUCCESS with a garbage vertex or E_FAILED, so the public GetVertex wrapper
    // must validate-first via EnsureValidCell, like CellToVertexes below.
    [LibraryImport("h3", EntryPoint = "cellToVertex")]
    internal static partial H3ErrorCode CellToVertex(ulong cell, int vertexNum, out ulong outVertex);

    // Fixed-capacity 6 (M4 = NUM_HEX_VERTS); no size call. A hexagon yields 6 valid
    // vertices; a pentagon yields 5 valid vertices plus one H3_NULL(0) slot, so
    // callers strip the H3_NULL entries. Does NOT validate its origin.
    [LibraryImport("h3", EntryPoint = "cellToVertexes")]
    internal static partial H3ErrorCode CellToVertexes(ulong cell, ulong* outVertices);

    // M2 reuse of the NativeLatLng out param; returns the VERTEX lat/lng (the upstream
    // header comment is a copy-paste error). Invalid vertex surfaces E_VERTEX_INVALID.
    [LibraryImport("h3", EntryPoint = "vertexToLatLng")]
    internal static partial H3ErrorCode VertexToLatLng(ulong vertex, out NativeLatLng point);

    // isValidVertex returns a bare C int (NOT H3Error) and never throws; it is the
    // validity predicate for a vertex index, so callers must NOT validate-first.
    [LibraryImport("h3", EntryPoint = "isValidVertex")]
    internal static partial int IsValidVertex(ulong vertex);

    // ---- Measures (area / length / distance / counts) ----------------------

    // M1 scalar by-ref + H3Error. Native does NOT fully validate the cell: a
    // malformed-but-in-range index returns E_SUCCESS with a garbage area rather than
    // E_CELL_INVALID, so the public CellArea* methods validate-first via EnsureValidCell.
    // Pentagons are valid.
    [LibraryImport("h3", EntryPoint = "cellAreaRads2")]
    internal static partial H3ErrorCode CellAreaRads2(ulong cell, out double area);

    [LibraryImport("h3", EntryPoint = "cellAreaKm2")]
    internal static partial H3ErrorCode CellAreaKm2(ulong cell, out double area);

    [LibraryImport("h3", EntryPoint = "cellAreaM2")]
    internal static partial H3ErrorCode CellAreaM2(ulong cell, out double area);

    // M1 scalar by-ref + H3Error. Native validates the resolution: a res outside 0-15
    // surfaces E_RES_DOMAIN. No rads2 average exists in the C API.
    [LibraryImport("h3", EntryPoint = "getHexagonAreaAvgKm2")]
    internal static partial H3ErrorCode GetHexagonAreaAvgKm2(int res, out double area);

    [LibraryImport("h3", EntryPoint = "getHexagonAreaAvgM2")]
    internal static partial H3ErrorCode GetHexagonAreaAvgM2(int res, out double area);

    // M1 scalar by-ref + H3Error. Native validates the resolution (E_RES_DOMAIN). No
    // rads average exists in the C API.
    [LibraryImport("h3", EntryPoint = "getHexagonEdgeLengthAvgKm")]
    internal static partial H3ErrorCode GetHexagonEdgeLengthAvgKm(int res, out double length);

    [LibraryImport("h3", EntryPoint = "getHexagonEdgeLengthAvgM")]
    internal static partial H3ErrorCode GetHexagonEdgeLengthAvgM(int res, out double length);

    // int64_t* out -> long. Native validates the resolution (E_RES_DOMAIN).
    [LibraryImport("h3", EntryPoint = "getNumCells")]
    internal static partial H3ErrorCode GetNumCells(int res, out long count);

    // M1 scalar by-ref + H3Error. Native does NOT fully validate the edge: a
    // malformed-but-in-range value (including a valid cell value that is not an edge)
    // can return E_SUCCESS with a garbage length rather than E_DIR_EDGE_INVALID, so the
    // public EdgeLength* methods validate-first via EnsureValid.
    [LibraryImport("h3", EntryPoint = "edgeLengthRads")]
    internal static partial H3ErrorCode EdgeLengthRads(ulong edge, out double length);

    [LibraryImport("h3", EntryPoint = "edgeLengthKm")]
    internal static partial H3ErrorCode EdgeLengthKm(ulong edge, out double length);

    [LibraryImport("h3", EntryPoint = "edgeLengthM")]
    internal static partial H3ErrorCode EdgeLengthM(ulong edge, out double length);

    // BARE double, NO H3Error: never throws from native. `in` marshals the const
    // LatLng* pointer. The native LatLng is in RADIANS; callers stage degrees->radians.
    [LibraryImport("h3", EntryPoint = "greatCircleDistanceRads")]
    internal static partial double GreatCircleDistanceRads(in NativeLatLng a, in NativeLatLng b);

    [LibraryImport("h3", EntryPoint = "greatCircleDistanceKm")]
    internal static partial double GreatCircleDistanceKm(in NativeLatLng a, in NativeLatLng b);

    [LibraryImport("h3", EntryPoint = "greatCircleDistanceM")]
    internal static partial double GreatCircleDistanceM(in NativeLatLng a, in NativeLatLng b);

    // BARE double, NO H3Error: never throws. Back the public LatLng.DegsToRads /
    // RadsToDegs angle-conversion helpers.
    [LibraryImport("h3", EntryPoint = "degsToRads")]
    internal static partial double DegsToRads(double degrees);

    [LibraryImport("h3", EntryPoint = "radsToDegs")]
    internal static partial double RadsToDegs(double radians);

    // ---- Corpus helpers ----------------------------------------------------

    [LibraryImport("h3", EntryPoint = "res0CellCount")]
    internal static partial int Res0CellCount();

    // out length must be >= res0CellCount().
    [LibraryImport("h3", EntryPoint = "getRes0Cells")]
    internal static partial H3ErrorCode GetRes0Cells(ulong* outCells);

    [LibraryImport("h3", EntryPoint = "pentagonCount")]
    internal static partial int PentagonCount();

    // out length must be >= pentagonCount().
    [LibraryImport("h3", EntryPoint = "getPentagons")]
    internal static partial H3ErrorCode GetPentagons(int res, ulong* outCells);
}
