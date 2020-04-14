using UnityEditor;
using UnityEngine;

namespace knockback
{
    public class TerrainComputeShader : MonoBehaviour
    {
        [System.Serializable]
        public struct NoiseParameters
        {
            public float offset;
            [Range(0, 20)]
            public float frequency;
            [Range(0, 1)]
            public float amplitude;
        }

        [System.Serializable]
        public struct ErosionSettings
        {
            public float minWaterDepthDisplay;
            public float rainMetersPerSecond;
            public float stepDuration;
            [Tooltip("How much water is lost each second in meters.")]
            public float waterLoss;
        }

        public struct TerrainCell
        {
            public float height;
            public Vector3 normal;
            public float waterDepth;
            public float debug;
        }

        ILogger log = Debug.unityLogger;

        ComputeBuffer rainBuffer1;
        ComputeBuffer rainBuffer2;
        int rainIteration = 0;

        public ComputeShader shader;

        public ErosionSettings erosionSettings = new ErosionSettings();

        [Range(-1, 1)]
        public float elevate = 0;
        [Range(0, 1)]
        public float inflectionPoint = 0.5f;
        [Range(0, 100)]
        public float steepness = 10;
        public NoiseParameters noise1;
        public NoiseParameters noise2;

        public Vector2 flatArea;
        [Range(0, 1)]
        public float flatSize;
        [Range(0, 1)]
        public float flatElevation;
        [Range(0, 1)]
        public float flatWeight;

        [Tooltip("The upper bound transition points in elevation between the various textures")]
        public float[] thresholds = new float[] { 0.1f, 0.3f, 0.5f, 0.7f, 1 };
        [Tooltip("The amount of overlap that should occur to blend between textures. A higher value helps to smooth the transitions between textures.")]
        [Range(0, 1)]
        public float thresholdOverlap = 0.05f;

        public Terrain waterTerrain
        {
            get
            {
                foreach (Transform c in transform)
                {
                    Terrain t = c.gameObject.GetComponent<Terrain>();
                    if (t) return t;
                }
                return null;
            }
        }

        public void Apply()
        {
            if (!enabled) return;

            Terrain t = GetComponent<Terrain>();
            if (!t)
            {
                Debug.LogWarning("TerrainComputeShader doesn't have terrain attached. Bailing.");
                return;
            }
            TerrainData td = t.terrainData;
            float[,] heights = td.GetHeights(0, 0, td.heightmapWidth, td.heightmapHeight);
            Apply(heights);
            td.SetHeights(0, 0, heights);

            PaintAlphaMap();
        }

        public void Apply(float[,] heights)
        {
            var start = EditorApplication.timeSinceStartup;

            int width = heights.GetLength(0);
            int length = heights.GetLength(1);
            Debug.Log($"{width} {length}");

            TerrainCell[] data = new TerrainCell[width * length];

            int cellSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TerrainCell));
            Debug.Log($"{width} {length} cellSize: {cellSize}");

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < length; j++)
                {
                    data[i + j * width].height = heights[i, j];
                }
            }

            ComputeBuffer buffer = new ComputeBuffer(data.Length, cellSize);
            buffer.SetData(data);
            int kernel = shader.FindKernel("CSMain");
            shader.SetBuffer(kernel, "working1", buffer);
            LoadShaderSettings();
            shader.Dispatch(kernel, width * length, 1, 1);
            buffer.GetData(data);
            buffer.Release();

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < length; j++)
                {
                    heights[i, j] = data[i + j * width].height;
                    //heights[i, j] = Mathf.Min(i, j);
                    //heights[i, j] = (float)i / (float)width;
                }
            }

            Debug.Log(EditorApplication.timeSinceStartup - start);
        }

        public Vector3[,] CalculateNormals(float[,] heights, float cellWidth)
        {
            var start = EditorApplication.timeSinceStartup;

            int width = heights.GetLength(0);
            int length = heights.GetLength(1);

            TerrainCell[] input = new TerrainCell[width * length];
            TerrainCell[] output = new TerrainCell[width * length];

            int cellSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TerrainCell));
            Debug.Log($"{width} {length} cellSize: {cellSize}");

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < length; j++)
                {
                    input[i + j * width].height = heights[i, j];
                }
            }

            ComputeBuffer inputBuffer = new ComputeBuffer(input.Length, cellSize);
            inputBuffer.SetData(input);
            ComputeBuffer outputBuffer = new ComputeBuffer(output.Length, cellSize);
            int kernel = shader.FindKernel("CalculateNormals");

            LoadShaderSettings();

            shader.SetInt("width", width);
            shader.SetBuffer(kernel, "working1", inputBuffer);
            shader.SetBuffer(kernel, "working2", outputBuffer);
            shader.SetFloat("cellWidth", cellWidth);

            var startDispatch = EditorApplication.timeSinceStartup;
            shader.Dispatch(kernel, input.Length, 1, 1);
            Debug.Log($"dispatch time: {EditorApplication.timeSinceStartup - startDispatch}");

            outputBuffer.GetData(output);
            inputBuffer.Release();
            outputBuffer.Release();

            Vector3[,] result = new Vector3[width, length];

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < length; j++)
                {
                    result[i, j] = output[i + j * width].normal;
                }
            }

            Debug.Log(EditorApplication.timeSinceStartup - start);

            return result;
        }

        public TerrainCell[,] CalculateWatershed(TerrainCell[,] input, float cellWidth,
            int iterations)
        {
            var start = EditorApplication.timeSinceStartup;

            int width = input.GetLength(0);
            int length = input.GetLength(1);
            Debug.Log($"{width} {length}");

            TerrainCell[,] output = new TerrainCell[width, length];

            int cellSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TerrainCell));

            ComputeBuffer inputBuffer = new ComputeBuffer(input.Length, cellSize);
            inputBuffer.SetData(input);
            ComputeBuffer outputBuffer = new ComputeBuffer(output.Length, cellSize);
            int kernel = shader.FindKernel("CalculateWatershed");

            LoadShaderSettings();

            shader.SetInt("width", width);
            shader.SetFloat("cellWidth", cellWidth);

            for (int i = 0; i < iterations; i++)
            {
                if (i % 2 == 0)
                {
                    shader.SetBuffer(kernel, "working1", inputBuffer);
                    shader.SetBuffer(kernel, "working2", outputBuffer);
                }
                else
                {
                    shader.SetBuffer(kernel, "working1", outputBuffer);
                    shader.SetBuffer(kernel, "working2", inputBuffer);
                }

                var startDispatch = EditorApplication.timeSinceStartup;
                shader.Dispatch(kernel, input.Length, 1, 1);
                Debug.Log($"dispatch time: {EditorApplication.timeSinceStartup - startDispatch}");
            }

            if (iterations % 2 == 1)
            {
                outputBuffer.GetData(output);
            }
            else
            {
                inputBuffer.GetData(output);
            }

            inputBuffer.Release();
            outputBuffer.Release();

            Debug.Log(EditorApplication.timeSinceStartup - start);

            return output;
        }

        void LoadShaderSettings()
        {
            if (!shader) return;

            Terrain t = GetComponent<Terrain>();
            if (t)
            {
                TerrainData td = t.terrainData;
                float cellWidth = td.size.x / td.heightmapWidth;

                shader.SetFloat("cellWidth", cellWidth);
                shader.SetInt("width", td.heightmapWidth);
            }

            shader.SetFloat("rainMetersPerSecond", erosionSettings.rainMetersPerSecond);
            shader.SetFloat("stepDuration", erosionSettings.stepDuration);
            shader.SetFloat("waterLoss", erosionSettings.waterLoss);

            shader.SetVector("flatArea", new Vector4(flatArea.x, flatArea.y, 0, 0));
            shader.SetFloat("flatSize", flatSize);
            shader.SetFloat("flatElevation", flatElevation);
            shader.SetFloat("flatWeight", flatWeight);

            shader.SetFloat("frequency1", noise1.frequency);
            shader.SetFloat("frequency2", noise2.frequency);
            shader.SetFloat("offset1", noise1.offset);
            shader.SetFloat("offset2", noise2.offset);
            shader.SetFloat("amplitude1", noise1.amplitude);
            shader.SetFloat("amplitude2", noise2.amplitude);
            shader.SetFloat("inflectionPoint", inflectionPoint);
            shader.SetFloat("elevate", elevate);
            shader.SetFloat("steepness", steepness);
        }

        TerrainCell[] LoadTerrainDataIntoArray()
        {
            Terrain t = GetComponent<Terrain>();
            TerrainData td = t.terrainData;

            float[,] heights = td.GetHeights(0, 0, td.heightmapWidth, td.heightmapHeight);

            TerrainCell[] input = new TerrainCell[td.heightmapWidth * td.heightmapHeight];
            int width = td.heightmapWidth;

            for (int x = 0; x < heights.GetLength(0); x++)
            {
                for (int z = 0; z < heights.GetLength(1); z++)
                {
                    input[x + z * width].height = heights[x, z];
                    input[x + z * width].waterDepth = 0;
                }
            }

            return input;
        }

        void LoadCellsIntoWaterTerrain(TerrainCell[] cells)
        {
            Terrain t = waterTerrain;
            TerrainData td = t.terrainData;

            float[,] heights = td.GetHeights(0, 0, td.heightmapWidth, td.heightmapHeight);

            for (int x = 0; x < heights.GetLength(0); x++)
            {
                for (int z = 0; z < heights.GetLength(1); z++)
                {
                    TerrainCell c = cells[x + z * td.heightmapWidth];
                    if (c.waterDepth < erosionSettings.minWaterDepthDisplay)
                    {
                        heights[x, z] = Mathf.Max(0.01f, c.height - 0.01f);
                    }
                    else
                    {
                        heights[x, z] = c.height + c.waterDepth / 10;
                    }
                }
            }

            td.SetHeights(0, 0, heights);
        }

        void PaintAlphaMap()
        {
            Terrain t = GetComponent<Terrain>();
            TerrainData td = t.terrainData;

            float[,,] alphaMap = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
            float[,] heights = td.GetHeights(0, 0, td.heightmapWidth, td.heightmapHeight);

            float[] thresholds = new float[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.8f };

            for (int x = 0; x < alphaMap.GetLength(0); x++)
            {
                for (int z = 0; z < alphaMap.GetLength(1); z++)
                {
                    float h = heights[x, z];

                    alphaMap[x, z, 0] = TextureWeight(h, 0);
                    alphaMap[x, z, 1] = TextureWeight(h, 1);
                    alphaMap[x, z, 2] = TextureWeight(h, 2);
                    alphaMap[x, z, 3] = TextureWeight(h, 3);
                    alphaMap[x, z, 4] = TextureWeight(h, 4);
                }
            }

            td.SetAlphamaps(0, 0, alphaMap);
        }

        /// OnValidate runs in the editor when a value changes.
        void OnValidate()
        {
            Apply();
            //RainOnce();
        }

        public void RainOnce()
        {
            if (!shader) return;

            if (rainBuffer1 == null) RestartRain();

            LoadShaderSettings();

            int cellSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TerrainCell));

            int kernel = shader.FindKernel("CalculateWatershed");

            if (rainIteration++ % 2 == 0)
            {
                shader.SetBuffer(kernel, "working1", rainBuffer1);
                shader.SetBuffer(kernel, "working2", rainBuffer2);
            }
            else
            {
                shader.SetBuffer(kernel, "working1", rainBuffer2);
                shader.SetBuffer(kernel, "working2", rainBuffer1);
            }

            Terrain t = waterTerrain;
            TerrainData td = t.terrainData;
            TerrainCell[] cells = new TerrainCell[td.heightmapWidth * td.heightmapHeight];

            var startDispatch = EditorApplication.timeSinceStartup;
            shader.Dispatch(kernel, cells.Length, 1, 1);
            log?.Log($"dispatch time: {EditorApplication.timeSinceStartup - startDispatch}");

            if (rainIteration % 2 == 1)
            {
                rainBuffer2.GetData(cells);
            }
            else
            {
                rainBuffer1.GetData(cells);
            }

            LoadCellsIntoWaterTerrain(cells);
        }

        public void RestartRain()
        {
            if (rainBuffer1 != null) rainBuffer1.Release();
            if (rainBuffer2 != null) rainBuffer2.Release();

            rainIteration = 0;

            int cellSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TerrainCell));

            TerrainCell[] terrainCells = LoadTerrainDataIntoArray();
            for (int i = 0; i < terrainCells.Length; i++)
            {
                terrainCells[i].waterDepth = 0;
            }

            rainBuffer1 = new ComputeBuffer(terrainCells.Length, cellSize);
            rainBuffer2 = new ComputeBuffer(terrainCells.Length, cellSize);
            rainBuffer1.SetData(terrainCells);
        }

        public void Start()
        {
            
        }

        float TextureWeight(float h, int index)
        {
            float lowerBound = 0;
            float upperBound = thresholds[index];

            if (index > 0) lowerBound = thresholds[index - 1];

            if (h < lowerBound - thresholdOverlap) return 0;
            if (h < lowerBound) return 1 - (lowerBound - h) / thresholdOverlap;

            if (h > upperBound + thresholdOverlap) return 0;
            if (h > upperBound) return 1 - (h - upperBound) / thresholdOverlap;

            return 1;
        }

    }
}
