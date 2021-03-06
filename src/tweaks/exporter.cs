using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RA3Tweaks.Tweaks
{
    public class DataExporter
    {
        public static string GetComponentsJson()
        {
            TextAsset textAsset = Resources.Load("Databases/Components") as TextAsset;
            return textAsset.text;
        }

        public static void ExportJson()
        {
            // Create the output path if nessessary
            string outputPath = Path.Combine(AssetsHandler.AssetDirectory, @".\exported");
            Debug.Log(outputPath);
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // Remove any older file
            string file = Path.Combine(outputPath, @".\components.json");
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            // Write out the json text to a file
            using (StreamWriter sr = new StreamWriter(file))
            {
                sr.Write(DataExporter.GetComponentsJson());
            }
        }
        
        public static void ExportModel(GameObject obj, string nameOverride = "")
        {
            // Create the output path if nessessary
            string outputPath = Path.Combine(AssetsHandler.AssetDirectory, @".\exported");
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            string name = string.IsNullOrEmpty(nameOverride) ? string.IsNullOrEmpty(obj.name) ? "model" : obj.name : nameOverride;
            string outputFile = Path.Combine(outputPath, @".\" + name + ".obj");
            ModelExporter.DoExport(obj, true, outputFile);
        }
    }


    public class ModelExporter : ScriptableObject
    {
        public static void DoExport(GameObject Selection, bool makeSubmeshes, string fileName)
        {
            string meshName = Selection.name;

            ModelExporterOutput.Start();
            {
                StringBuilder meshString = new StringBuilder();
                meshString.Append("#" + meshName + ".obj"
                                      + "\n#" + System.DateTime.Now.ToLongDateString()
                                      + "\n#" + System.DateTime.Now.ToLongTimeString()
                                      + "\n#-------"
                                      + "\n\n");

                Transform t = Selection.transform;
                Vector3 originalPosition = t.position;
                t.position = Vector3.zero;

                if (!makeSubmeshes)
                {
                    meshString.Append("g ").Append(t.name).Append("\n");
                }
                meshString.Append(processTransform(t, makeSubmeshes));

                WriteToFile(meshString.ToString(), fileName);

                t.position = originalPosition;
            }
            ModelExporterOutput.End();

            Debug.Log("Exported Mesh: " + fileName);
        }

        static string processTransform(Transform t, bool makeSubmeshes)
        {
            StringBuilder meshString = new StringBuilder();
            meshString.Append("#" + t.name
                                  + "\n#-------"
                                  + "\n");

            if (makeSubmeshes)
            {
                meshString.Append("g ").Append(t.name).Append("\n");
            }

            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf)
            {
                meshString.Append(ModelExporterOutput.MeshToString(mf, t));
            }

            for (int i = 0; i < t.childCount; i++)
            {
                meshString.Append(processTransform(t.GetChild(i), makeSubmeshes));
            }

            return meshString.ToString();
        }

        static void WriteToFile(string s, string filename)
        {
            using (StreamWriter sw = new StreamWriter(filename))
            {
                sw.Write(s);
            }
        }
    }

    public class ModelExporterOutput
    {
        private static int StartIndex = 0;

        public static void Start()
        {
            StartIndex = 0;
        }

        public static void End()
        {
            StartIndex = 0;
        }

        public static string MeshToString(MeshFilter mf, Transform t)
        {
            Vector3 s = t.localScale;
            Vector3 p = t.localPosition;
            Quaternion r = t.localRotation;

            int numVertices = 0;
            Mesh m = mf.sharedMesh;
            if (!m)
            {
                return "####Error####";
            }
            Material[] mats = mf.GetComponent<Renderer>().sharedMaterials;

            StringBuilder sb = new StringBuilder();

            foreach (Vector3 vv in m.vertices)
            {
                Vector3 v = t.TransformPoint(vv);
                numVertices++;
                sb.Append(string.Format("v {0} {1} {2}\n", v.x, v.y, -v.z));
            }
            sb.Append("\n");
            foreach (Vector3 nn in m.normals)
            {
                Vector3 v = r * nn;
                sb.Append(string.Format("vn {0} {1} {2}\n", -v.x, -v.y, v.z));
            }
            sb.Append("\n");
            foreach (Vector3 v in m.uv)
            {
                sb.Append(string.Format("vt {0} {1}\n", v.x, v.y));
            }
            for (int material = 0; material < m.subMeshCount; material++)
            {
                sb.Append("\n");
                sb.Append("usemtl ").Append(mats[material].name).Append("\n");
                sb.Append("usemap ").Append(mats[material].name).Append("\n");

                int[] triangles = m.GetTriangles(material);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    sb.Append(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",
                        triangles[i] + 1 + StartIndex, triangles[i + 1] + 1 + StartIndex, triangles[i + 2] + 1 + StartIndex));
                }
            }

            StartIndex += numVertices;
            return sb.ToString();
        }
    }
}