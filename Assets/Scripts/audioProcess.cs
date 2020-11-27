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
        private int MaxFrequency = 5000;

        public int maxRows;
        [Header("Number of FFT samples to take")]
        public int width;
        [Header("Amp scale factor")]
        public float ampScaleFactor;
        public bool smoothPeaks;
        [Header("Spacing between rows of vertices")]
        public float zSpacing;
        [Header("Camera FOV Scale Factor")]
        public float fovScale;

        private List<Vector3> m_vertices;
        private List<float> m_amplitudes;
        private List<int> m_triangles;
        private int m_curZ;
        private float m_xLeft;
        private int m_trueWidth;
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
            // Init mesh
            m_meshFilter = GetComponent<MeshFilter>();
            m_mesh = new Mesh();
            m_mesh.MarkDynamic();
            m_mesh.name = "spectrogram";
            m_meshFilter.mesh = m_mesh;

            // Find leftmost x point centered around 0
            m_curZ = 0;
            m_trueWidth = width / 2;

            m_vertices = new List<Vector3>();
            m_amplitudes = new List<float>();
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

            if (smoothPeaks)
            {
                smoothArray(m_spectrumData);
            }

            // Smooth out past values in same freq bins
            if (m_curZ > 0)
            {
                int curRow = (m_curZ > maxRows) ? maxRows : m_curZ;
                for (int i = 0; i < width; i++)
                {
                    if (i >= m_trueWidth)
                    {
                        int trueX = i - m_trueWidth;
                        float prevAmp = m_amplitudes[(width * (curRow - 1)) + trueX];
                        m_spectrumData[i] = Mathf.Lerp(prevAmp, m_spectrumData[trueX], .2F);
                    } else
                    {
                        float prevAmp = m_amplitudes[(width * (curRow - 1)) + i];
                        m_spectrumData[i] = Mathf.Lerp(prevAmp, m_spectrumData[i], .2F);
                    }
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
            for (int i = 0; i < width; i++)
            {
                if (i < m_trueWidth)
                {
                    m_vertices.Add(calcVertex(i, spectrum[i]));
                } else
                {
                    m_vertices.Add(calcVertex(i, spectrum[i - m_trueWidth]));
                }
                
            }

            m_mesh.vertices = m_vertices.ToArray();
        }

        private void generateColors()
        {
            int curRow = m_curZ > maxRows ? maxRows : m_curZ;

            // Start at new row, make colors for new vertices
            for (int vertexIndex = width * curRow, x = 0; x < width; x++, vertexIndex++)
            {
                float normAmp = m_amplitudes[vertexIndex];
                int trueX = x;
                if (x >= m_trueWidth)
                {
                    trueX = x - m_trueWidth;
                }
                normAmp += 3 * (trueX / m_trueWidth);
                normAmp = Mathf.Min(normAmp, 1.0f);
                normAmp = Mathf.Max(0, normAmp);
                m_colors.Add(getRainbowColor(normAmp));
            }

            m_mesh.colors = m_colors.ToArray();
        }

        private Color getRainbowColor(float value)
        {
            /*convert to long rainbow RGB*/
            value += m_curZ * 0.0001f;
            //value += m_curRms;
            value = value % 1.0F;
            float a = (1.0F - value) * 6;
            int X = (int)Mathf.Floor(a);
            float Y = a - X;
            float r, g, b;
            switch (X)
            {
                case 0: r = 1; g = Y; b = 0; break;
                case 1: r = 1 - Y; g = 1; b = 0; break;
                case 2: r = 0; g = 1; b = Y; break;
                case 3: r = 0; g = 1 - Y; b = 1; break;
                case 4: r = Y; g = 0; b = 1; break;
                case 5: r = 1; g = 0; b = 1 - Y; break;
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
            for (int vertexIndex = width * (curRow - 1), x = 0; x < width - 1; x++, vertexIndex++)
            {
                m_triangles.Add(vertexIndex);
                m_triangles.Add(vertexIndex + width);
                m_triangles.Add(vertexIndex + 1);
                m_triangles.Add(vertexIndex + 1);
                m_triangles.Add(vertexIndex + width);
                m_triangles.Add(vertexIndex + width + 1);
            }

            // Add last 2 triangles to connect cylinder
            int startIdx = width * (curRow - 1);
            m_triangles.Add(startIdx);
            m_triangles.Add(startIdx + width + width - 1);
            m_triangles.Add(startIdx + width);
            m_triangles.Add(startIdx);
            m_triangles.Add(startIdx + width - 1);
            m_triangles.Add(startIdx + width + width - 1);

            m_mesh.triangles = m_triangles.ToArray();
            m_mesh.RecalculateNormals();
        }

        private Vector3 calcVertex(int i, float amp)
        {
            Vector3 v = new Vector3(0, 0, 0);
            float cx = 0; float cy = 5;
            float scaleFactor = 1.3f;
            if (i < m_trueWidth)
            {
                scaleFactor += Mathf.Exp(i - m_trueWidth);
            }
            else
            {
                scaleFactor += Mathf.Exp((i - m_trueWidth) - m_trueWidth);
            }
            float radius = ampScaleFactor - (amp * scaleFactor * ampScaleFactor);
            m_amplitudes.Add(amp); // Add amplitude to list
            float angle = (i / (float)(width - 1)) * 2.0f * Mathf.PI + m_curZ * 0.002f;
            angle = angle % (2 * Mathf.PI);
            float x = cx + radius * Mathf.Cos(angle);
            float y = cy + radius * Mathf.Sin(angle);
            return new Vector3(x, y, m_curZ * zSpacing);
        }

        private void removeOldRows()
        {
            m_vertices.RemoveRange(0, width);
            m_amplitudes.RemoveRange(0, width);
            m_colors.RemoveRange(0, width);
            m_triangles.RemoveRange(0, 6 * width); // 6 points per tile
            for (int i = 0; i < m_triangles.Count; i++)
            {
                m_triangles[i] -= width;
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
            newRms = Mathf.Lerp(m_curRms, newRms, 0.2F);
            Camera.main.fieldOfView = 88.0F + (newRms * fovScale);
            m_curRms = newRms;
        }

        // Function that Calculate Root Mean Square of spectrum data
        private float rmsValue()
        {
            float square = 0;
            float mean = 0;
            float root = 0;
            int n = width;

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