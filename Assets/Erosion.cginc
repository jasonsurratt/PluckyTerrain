
// GetCell returns the specified cell. If the requested cell is out of range, the nearest pixel
// value will be returned.
struct TerrainCell GetCell(RWStructuredBuffer<TerrainCell> cells, int px, int py, int width)
{
	px = clamp(px, 0, width - 1);
	py = clamp(py, 0, width - 1);

	return cells[px + py * width];
}

// Calculates the normal to a given pixel. This normal is calculated using
// Horn's formula [1].
//
// This assumes that the raster is square.
//
// 1. Horn, B. K. P. (1981). Hill Shading and the Reflectance Map, Proceedings
// of the IEEE, 69(1):14-47.
// http://scholar.google.com/scholar?cluster=13504326307708658108&hl=en
float3 CalculateHornNormal(RWStructuredBuffer<TerrainCell> cells, int2 pos, int width,
	float cellWidth)
{
	// using Horn's notation of Z-+, Z0+, etc. but - == n & + == p

	int px = pos.x;
	int py = pos.y;

	float znp = GetCell(cells, px - 1, py + 1, width).height;
	float z0p = GetCell(cells, px + 0, py + 1, width).height;
	float zpp = GetCell(cells, px + 1, py + 1, width).height;
	float zn0 = GetCell(cells, px - 1, py + 0, width).height;
	float z00 = GetCell(cells, px + 0, py + 0, width).height;
	float zp0 = GetCell(cells, px + 1, py + 0, width).height;
	float znn = GetCell(cells, px - 1, py - 1, width).height;
	float z0n = GetCell(cells, px + 0, py - 1, width).height;
	float zpn = GetCell(cells, px + 1, py - 1, width).height;

	// horizontal slope
	float pw = ((zpp + 2.0 * zp0 + zpn) - (znp + 2.0 * zn0 + znn)) / (8.0 * cellWidth);
	// vertical slope
	float qw = ((zpp + 2.0 * z0p + znp) - (zpn + 2.0 * z0n + znn)) / (8.0 * cellWidth);

	float3 result = {-pw, 1, -qw};

	return normalize(result);
	//return result;
}

// CalculateHornNormal calculates and returns the horn normal for a cell. This assumes that the
// raster is square.
float3 CalculateWaterHornNormal(RWStructuredBuffer<TerrainCell> cells, int2 pos, int width,
	float cellWidth)
{
	// using Horn's notation of Z-+, Z0+, etc. but - == n & + == p

	int px = pos.x;
	int py = pos.y;
	int z00i = ((uint)px) % width + ((uint)py) / width;

	TerrainCell znpC = GetCell(cells, px - 1, py + 1, width);
	TerrainCell z0pC = GetCell(cells, px + 0, py + 1, width);
	TerrainCell zppC = GetCell(cells, px + 1, py + 1, width);
	TerrainCell zn0C = GetCell(cells, px - 1, py + 0, width);
	TerrainCell z00C = GetCell(cells, px + 0, py + 0, width);
	TerrainCell zp0C = GetCell(cells, px + 1, py + 0, width);
	TerrainCell znnC = GetCell(cells, px - 1, py - 1, width);
	TerrainCell z0nC = GetCell(cells, px + 0, py - 1, width);
	TerrainCell zpnC = GetCell(cells, px + 1, py - 1, width);

	float znp = znpC.height + znpC.waterDepth;
	float z0p = z0pC.height + z0pC.waterDepth;
	float zpp = zppC.height + zppC.waterDepth;
	float zn0 = zn0C.height + zn0C.waterDepth;
	float z00 = z00C.height + z00C.waterDepth;
	float zp0 = zp0C.height + zp0C.waterDepth;
	float znn = znnC.height + znnC.waterDepth;
	float z0n = z0nC.height + z0nC.waterDepth;
	float zpn = zpnC.height + zpnC.waterDepth;

	// horizontal slope
	float pw = ((zpp + 2 * zp0 + zpn) - (znp + 2 * zn0 + znn)) / (8 * cellWidth);
	// vertical slope
	float qw = ((zpp + 2 * z0p + znp) - (zpn + 2 * z0n + znn)) / (8 * cellWidth);

	float3 result = {-pw, 1, -qw};
 
	return normalize(result);
}

float CalculateWaterContribution(RWStructuredBuffer<TerrainCell> cells, int2 from, int2 to,
	uint width, float cellWidth)
{
	if (from.x < 0 || (uint)from.x > width - 1 || from.y < 0 || (uint)from.y > width - 1)
	{
		return 0;
	}

	int2 delta = { to.x - from.x, to.y - from.y };

	float3 normal = CalculateWaterHornNormal(cells, from, width, cellWidth);

	// This is all horribly fudged. Maybe revisit?
	// http://users.encs.concordia.ca/~grogono/Graphics/fluid-5.pdf
	if (normal.x + normal.z == 0) return 0;

	float thisContribution;
	if (delta.x != 0)
	{
		if (delta.x < 0 && normal.x > 0) return 0;
		if (delta.x > 0 && normal.x < 0) return 0;

		thisContribution = normal.x / (normal.x + normal.z + normal.y);
	}
	else
	{
		if (delta.y < 0 && normal.z > 0) return 0;
		if (delta.y > 0 && normal.z < 0) return 0;

		thisContribution = normal.z / (normal.x + normal.z + normal.y);
	}

	return thisContribution * GetCell(cells, from.x, from.y, width).waterDepth;
}
