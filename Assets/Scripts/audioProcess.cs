using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Assets.WasapiAudio.Scripts.Core;
using Assets.WasapiAudio.Scripts.Wasapi;

namespace Assets.WasapiAudio.Scripts.Unity
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(AudioListener))]
    public class audioProcess : MonoBehaviour
    {
        private Mesh m_mesh;
        private List<Color> m_colors;
        private MeshFilter m_meshFilter;
        private Wasapi.WasapiAudio _wasapiAudio;

        private WasapiCaptureType CaptureType = WasapiCaptureType.Loopback;
        private ScalingStrategy ScalingStrategy = ScalingStrategy.Linear;
        private int MinFrequency = 80;
        private int MaxFrequency = 20000;

        public int maxRows;
        [Header("Number of FFT samples to take")]
        public int width;
        [Header("Amp scale factor")]
        public float ampScaleFactor;
        public bool smoothPeaks;
        [Header("Spacing between sample vertices")]
        public float xSpacing;
        [Header("Spacing between rows of vertices")]
        public float zSpacing;
        [Header("Camera FOV Scale Factor")]
        public float fovScale;

        private List<Vector3> m_vertices;
        private List<int> m_triangles;
        private int m_curZ;
        private float m_xLeft;
        private int m_divFactor;
        private int m_trueWidth;
        private int m_rowWidth;
        private float[] m_spectrumData;
        private float m_curRms;

        public void Awake()
        {
            // Setup loopback audio and start listening
            _wasapiAudio = new Wasapi.WasapiAudio(CaptureType, width, ScalingStrategy, MinFrequency, MaxFrequency, spectrumData =>
            {
                m_spectrumData = spectrumData;
            });

            _wasapiAudio.StartListen();
        }

        // Start is called before the first frame update
        void Start()
        {
            m_divFactor = 3;
            m_trueWidth = width / m_divFactor;
            m_rowWidth = m_trueWidth * 2;

            // Init mesh
            m_meshFilter = GetComponent<MeshFilter>();
            m_mesh = new Mesh();
            m_mesh.name = "spectrogram";
            m_meshFilter.mesh = m_mesh;

            // Find leftmost x point centered around 0
            m_xLeft = 0 - (xSpacing * m_trueWidth);
            m_curZ = 0;

            m_vertices = new List<Vector3>();
            m_triangles = new List<int>();
            m_colors = new List<Color>();

            m_curRms = 0;
        }

        // Update is called once per frame
        void Update()
        {
            moveCamera();

            RenderSettings.skybox.SetFloat("_Rotation", Time.time * 0.6f);

            // Clean up unseen verts and tris
            if (m_curZ > maxRows)
            {
                removeOldRows();
            }

            //float[] spectrum = new float[width];

            //AudioListener.GetSpectrumData(spectrum, 0, FFTWindow.Rectangular);

            if (smoothPeaks)
            {
                smoothArray(m_spectrumData);
            }

            // Smooth out past values in same freq bins
            if (m_curZ > 0)
            {
                int curRow = (m_curZ > maxRows) ? maxRows : m_curZ;
                for (int i = 0; i < m_rowWidth; i++)
                {
                    float prevAmp = m_vertices[(m_rowWidth * (curRow - 1)) + i].y / ampScaleFactor;
                    m_spectrumData[i] = Mathf.Lerp(prevAmp, m_spectrumData[i], .2F);
                }
            }

            generateVertices(m_spectrumData); // Get vertices for spectrum
            generateColors();
            generateTriangles(); // Generate triangles connecting current and past row
            m_curZ++; // Get next row index

            m_mesh.RecalculateBounds();
            m_mesh.RecalculateTangents();
        }

        private void generateVertices(float[] spectrum)
        {
            // Generate half of the normal vertices
            for (int i = 0; i < m_trueWidth; i++)
            {
                m_vertices.Add(calcVertex(i, spectrum[i]));
            }

            // Generate half of the flipped vertices
            for (int i = 0; i < m_trueWidth; i++)
            {
                m_vertices.Add(calcFlippedVertex(i, spectrum[m_trueWidth - 1 - i]));
            }


            m_mesh.vertices = m_vertices.ToArray();
        }

        private void generateColors()
        {
            int curRow = m_curZ > maxRows ? maxRows : m_curZ;

            float maxAmp = 1.0f; // Tweak this to get more/less colors
            float scaleFactor = .004f;

            // Start at new row, make triangles for new vertices
            for (int vertexIndex = m_rowWidth * curRow, x = 0; x < m_rowWidth; x++, vertexIndex++)
            {
                float normAmp = m_vertices[vertexIndex].y / maxAmp;
                int trueX = x;
                if (x >= m_trueWidth)
                {
                    trueX = (m_rowWidth - 1) - x;
                }
                normAmp += (trueX * scaleFactor);
                normAmp = Mathf.Min(normAmp, 1.0f);
                m_colors.Add(getRainbowColor(normAmp));
            }

            m_mesh.colors = m_colors.ToArray();
        }

        private Color getRainbowColor(float value)
        {
            /*convert to long rainbow RGB*/
            value += m_curRms;
            value = value % 1.0F;
            float a = (1.0F - value) * 6;
            int X = (int)Mathf.Floor(a);
            int Y = (int)Mathf.Floor(a - X);
            float r, g, b;
            switch (X)
            {
                case 0: r = 1; g = Y; b = 0; break;
                case 1: r = 1 - Y; g = 1; b = 0; break;
                case 2: r = 0; g = 1; b = Y; break;
                case 3: r = 0; g = 1 - Y; b = 1; break;
                case 4: r = Y; g = 0; b = 1; break;
                case 5: r = 1; g = 0; b = 1; break;
                default:
                {
                        r = 0; g = 0; b = 0; break;
                }
            }
            return new Color(r, g, b);
        }

        private void generateTriangles()
        {
            if (m_curZ == 0)
            {
                return; // Skip if only 1 row has been made
            }

            int curRow = m_curZ > maxRows ? maxRows : m_curZ;

            // Start at new row, make triangles for new vertices
            for (int vertexIndex = m_rowWidth * (curRow - 1), x = 0; x < m_rowWidth - 1; x++, vertexIndex++)
            {
                m_triangles.Add(vertexIndex);
                m_triangles.Add(vertexIndex + m_rowWidth);
                m_triangles.Add(vertexIndex + 1);
                m_triangles.Add(vertexIndex + 1);
                m_triangles.Add(vertexIndex + m_rowWidth);
                m_triangles.Add(vertexIndex + m_rowWidth + 1);
            }

            m_mesh.triangles = m_triangles.ToArray();
            m_mesh.RecalculateNormals();
        }

        private Vector3 calcVertex(int i, float amp)
        {
            return new Vector3(m_xLeft + (xSpacing * i), amp * ampScaleFactor, m_curZ * zSpacing);
        }
        private Vector3 calcFlippedVertex(int i, float amp)
        {
            return new Vector3((xSpacing * i), amp * ampScaleFactor, m_curZ * zSpacing);
        }

        private void removeOldRows()
        {
            m_vertices.RemoveRange(0, m_rowWidth);
            m_colors.RemoveRange(0, m_rowWidth);
            m_triangles.RemoveRange(0, 6 * (m_rowWidth - 1)); // 6 points per tile
            for (int i = 0; i < m_triangles.Count; i++)
            {
                m_triangles[i] -= m_rowWidth;
            }
        }

        private void smoothArray(float[] src)
        {
            for (int i = 1; i < src.Length; i++)
            {
                //---------------------------------------------------------------avg
                var start = (i - 1 > 0 ? i - 1 : 0);
                var end = (i + 1 < src.Length ? i + 1 : src.Length);

                float sum = 0;

                for (int j = start; j < end; j++)
                {
                    sum += src[j];
                }

                var avg = sum / (end - start);
                //---------------------------------------------------------------
                src[i] = avg;

            }
        }

        private void moveCamera()
        {
            Camera.main.transform.parent.Translate(new Vector3(0, 0, 1) * zSpacing);
            float newRms = rmsValue();
            Mathf.Lerp(m_curRms, newRms, 0.2F);
            Camera.main.fieldOfView = 60.0F + (newRms * fovScale);
            m_curRms = newRms;
        }

        // Function that Calculate Root Mean Square of spectrum data
        private float rmsValue()
        {
            float square = 0;
            float mean = 0;
            float root = 0;
            int n = m_rowWidth / 2;

            // Calculate square. 
            for (int i = 0; i < n; i++)
            {
                square += Mathf.Pow(m_spectrumData[i], 2);
            }

            // Calculate Mean. 
            mean = (square / (float)(n));

            // Calculate Root. 
            root = Mathf.Sqrt(mean);

            return root;
        }

    }
}