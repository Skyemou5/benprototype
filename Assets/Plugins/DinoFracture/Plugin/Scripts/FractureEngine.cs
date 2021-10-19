using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DinoFracture
{
    /// <summary>
    /// Argument passed to OnFracture message
    /// </summary>
    public sealed class OnFractureEventArgs
    {
        public OnFractureEventArgs(FractureGeometry orig, GameObject root)
        {
            OriginalObject = orig;
            FracturePiecesRootObject = root;
        }

        /// <summary>
        /// The object that fractured.
        /// </summary>
        public FractureGeometry OriginalObject;

        /// <summary>
        /// The root of the pieces of the resulting fracture.
        /// </summary>
        public GameObject FracturePiecesRootObject;
    }

    /// <summary>
    /// The result of a fracture.
    /// </summary>
    public sealed class AsyncFractureResult
    {
        /// <summary>
        /// Returns true if the operation has finished; false otherwise.
        /// This value will always be true for synchronous fractures.
        /// </summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// Returns true if the operation has finished and returned valid results.
        /// </summary>
        public bool IsSuccessful
        {
            get { return IsComplete && PiecesRoot != null; }
        }

        /// <summary>
        /// The root of the pieces of the resulting fracture
        /// </summary>
        public GameObject PiecesRoot { get; private set; }

        /// <summary>
        /// The bounds of the original mesh
        /// </summary>
        public Bounds EntireMeshBounds { get; private set; }

        internal bool StopRequested { get; private set; }

        internal void SetResult(GameObject rootGO, Bounds bounds)
        {
            if (IsComplete)
            {
                UnityEngine.Debug.LogWarning("DinoFracture: Setting AsyncFractureResult's results twice.");
            }
            else
            {
                PiecesRoot = rootGO;
                EntireMeshBounds = bounds;
                IsComplete = true;
            }
        }

        public void StopFracture()
        {
            StopRequested = true;
        }
    }

    /// <summary>
    /// This component is created on demand to manage the fracture coroutines.
    /// It is not intended to be added by the user.
    /// </summary>
    public sealed class FractureEngine : MonoBehaviour
    {
        private struct FractureInstance
        {
            public AsyncFractureResult Result;
            public IEnumerator Enumerator;

            public FractureInstance(AsyncFractureResult result, IEnumerator enumerator)
            {
                Result = result;
                Enumerator = enumerator;
            }
        }

        private static FractureEngine _instance;

        private bool _suspended;

        private int _maxRunningFractures = 0;

        private List<FractureInstance> _runningFractures = new List<FractureInstance>();
        private List<FractureInstance> _pendingFractures = new List<FractureInstance>();

        private static FractureEngine Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject inst = new GameObject("Fracture Engine");
                    _instance = inst.AddComponent<FractureEngine>();
                }

                return _instance;
            }
        }

        /// <summary>
        /// True if all further fracture operations should be a no-op.
        /// </summary>
        public static bool Suspended
        {
            get { return Instance._suspended; }
            set { Instance._suspended = value; }
        }

        /// <summary>
        /// Returns true if there are fractures currently in progress
        /// </summary>
        public static bool HasFracturesInProgress
        {
            get { return Instance._runningFractures.Count > 0; }
        }

        /// <summary>
        /// The maximum number of async fractures we can process at a time.
        /// If this is set to 0 (default), an unlimited number can be run.
        /// </summary>
        /// <remarks>
        /// NOTE: Synchronous fractures always run immediately
        /// </remarks>
        public static int MaxRunningFractures
        {
            get { return Instance._maxRunningFractures; }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Starts a fracture operation
        /// </summary>
        /// <param name="details">Fracture info</param>
        /// <param name="callback">The object to fracture</param>
        /// <param name="piecesParent">The parent of the resulting fractured pieces root object</param>
        /// <param name="transferMass">True to distribute the original object's mass to the fracture pieces; false otherwise</param>
        /// <param name="hideAfterFracture">True to hide the originating object after fracturing</param>
        /// <returns></returns>
        public static AsyncFractureResult StartFracture(FractureDetails details, FractureGeometry callback, Transform piecesParent, bool transferMass, bool hideAfterFracture)
        {
            AsyncFractureResult res = new AsyncFractureResult();
            if (Suspended)
            {
                res.SetResult(null, new Bounds());
            }
            else
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                details.Asynchronous = false;
#endif

                IEnumerator it = Instance.WaitForResults(FractureBuilder.Fracture(details), callback, piecesParent, transferMass, hideAfterFracture, res);

                if (details.Asynchronous)
                {
                    if (MaxRunningFractures <= 0 || Instance._runningFractures.Count < MaxRunningFractures)
                    {
                        if (it.MoveNext())
                        {
#if UNITY_EDITOR
                            if (Instance._runningFractures.Count == 0 && !Application.isPlaying)
                            {
                                EditorApplication.update += Instance.OnEditorUpdate;
                            }
#endif
                            Instance._runningFractures.Add(new FractureInstance(res, it));
                        }
                    }
                    else
                    {
                        Instance._pendingFractures.Add(new FractureInstance(res, it));
                    }
                }
                else
                {
                    // There should only be one iteration
                    while (it.MoveNext())
                    {
                        Debug.LogWarning("DinoFracture: Sync fracture taking more than one iteration");
                    }
                }
            }
            return res;
        }

        private void OnEditorUpdate()
        {
            UpdateFractures();

            if (_runningFractures.Count == 0)
            {
#if UNITY_EDITOR
                EditorApplication.update -= OnEditorUpdate;
#endif
                DestroyImmediate(gameObject);
            }
        }

        private void Update()
        {
            UpdateFractures();
        }

        private void UpdateFractures()
        {
            for (int i = _runningFractures.Count - 1; i >= 0; i--)
            {
                if (_runningFractures[i].Result.StopRequested)
                {
                    _runningFractures.RemoveAt(i);
                }
                else
                {
                    if (!_runningFractures[i].Enumerator.MoveNext())
                    {
                        _runningFractures.RemoveAt(i);
                    }
                }
            }

            for (int i = 0; i < _pendingFractures.Count; i++)
            {
                if (_runningFractures.Count < _maxRunningFractures)
                {
                    _runningFractures.Add(_pendingFractures[i]);
                    _pendingFractures.RemoveAt(i);
                    i--;
                }
            }
        }

        private IEnumerator WaitForResults(AsyncFractureOperation operation, FractureGeometry callback, Transform piecesParent, bool transferMass, bool hideAfterFracture, AsyncFractureResult result)
        {
            while (!operation.IsComplete)
            {
                // Async fractures should not happen while in edit mode because game objects don't update too often
                // and the coroutine is not pumped. Sync fractures should not reach this point.
                System.Diagnostics.Debug.Assert(Application.isPlaying && operation.Details.Asynchronous);
                yield return null;
            }

            if (callback == null)
            {
                result.SetResult(null, new Bounds());
                yield break;
            }

            Rigidbody origBody = null;
            if (transferMass)
            {
                origBody = callback.GetComponent<Rigidbody>();
            }
            float density = 0.0f;
            if (origBody != null)
            {
                Collider collider = callback.GetComponent<Collider>();
                if (collider != null && collider.enabled)
                {
                    // Calculate the density by setting the density to
                    // a known value and see what the mass comes out to.
                    float mass = origBody.mass;
                    origBody.SetDensity(1.0f);
                    float volume = origBody.mass;
                    density = mass / volume;

                    // Reset the mass
                    origBody.mass = mass;
                }
                else
                {
                    // Estimate the density based on the size of the object
                    Bounds bounds = operation.Details.Mesh.bounds;
                    float volume = bounds.size.x * operation.Details.MeshScale.x * bounds.size.y * operation.Details.MeshScale.y * bounds.size.z * operation.Details.MeshScale.z;
                    density = origBody.mass / volume;
                }
            }

            List<FracturedMesh> meshes = operation.Result.GetMeshes();

            GameObject rootGO = new GameObject(callback.gameObject.name + " - Fracture Root");
            rootGO.transform.parent = (piecesParent ?? callback.transform.parent);
            rootGO.transform.position = callback.transform.position;
            rootGO.transform.rotation = callback.transform.rotation;
            rootGO.transform.localScale = Vector3.one;  // Scale is controlled by the value in operation.Details

            Material[] sharedMaterials = callback.GetComponent<Renderer>().sharedMaterials;

            for (int i = 0; i < meshes.Count; i++)
            {
                GameObject go = (GameObject)Instantiate(callback.FractureTemplate);
                go.name = "Fracture Object " + i;
                go.transform.parent = rootGO.transform;
                go.transform.localPosition = meshes[i].Offset;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.SetActive(true);

                MeshFilter mf = go.GetComponent<MeshFilter>();
                mf.mesh = meshes[i].Mesh;

                // Copy the correct materials to the new mesh.
                // There are some things we need to account for:
                //
                // 1) Not every subMesh in the original mesh will still
                //    exist. It may have no triangles now and we should
                //    skip over those materials.
                // 2) We have added a new submesh for the inside triangles
                //    and need to add the inside material.
                // 3) The original mesh might have more materials than
                //    were subMeshes. In that case, we want to append
                //    the extra materials to the end of our list.
                //
                // The final material list will be:
                // * Used materials from the original mesh
                // * Inside material
                // * Extra materials from the original mesh
                MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    // There is an entry in EmptyTriangles for each subMesh,
                    // including the newly added inside triangles. The last
                    // subMesh is always the inside triangles we created.
                    int numOrigSubMeshes = meshes[i].EmptyTriangles.Count - 1;

                    Material[] materials = new Material[sharedMaterials.Length - meshes[i].EmptyTriangleCount + 1];
                    int matIdx = 0;
                    for (int m = 0; m < numOrigSubMeshes; m++)
                    {
                        if (!meshes[i].EmptyTriangles[m])
                        {
                            materials[matIdx++] = sharedMaterials[m];
                        }
                    }
                    if (!meshes[i].EmptyTriangles[numOrigSubMeshes])
                    {
                        materials[matIdx++] = callback.InsideMaterial;
                    }
                    for (int m = numOrigSubMeshes; m < sharedMaterials.Length; m++)
                    {
                        materials[matIdx++] = sharedMaterials[m];
                    }

                    meshRenderer.sharedMaterials = materials;
                }

                MeshCollider meshCol = go.GetComponent<MeshCollider>();
                if (meshCol != null)
                {
                    meshCol.sharedMesh = mf.sharedMesh;
                }

                if (transferMass && origBody != null)
                {
                    Rigidbody rb = go.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.SetDensity(density);
                        rb.mass = rb.mass;  // Need to explicity set it for the editor to reflect the changes
                    }
                }

                FractureGeometry fg = go.GetComponent<FractureGeometry>();
                if (fg != null)
                {
                    fg.InsideMaterial = callback.InsideMaterial;
                    fg.FractureTemplate = callback.FractureTemplate;
                    fg.PiecesParent = callback.PiecesParent;
                    fg.NumGenerations = callback.NumGenerations - 1;
                    fg.DistributeMass = callback.DistributeMass;
                }
            }

            OnFractureEventArgs args = new OnFractureEventArgs(callback, rootGO);
            if (Application.isPlaying)
            {
                callback.gameObject.SendMessage("OnFracture", args, SendMessageOptions.DontRequireReceiver);
            }
            else
            {
                callback.OnFracture(args);
            }

            if (hideAfterFracture)
            {
                callback.gameObject.SetActive(false);
            }

            if (Application.isPlaying)
            {
                Transform trans = rootGO.transform;
                for (int i = 0; i < trans.childCount; i++)
                {
                    trans.GetChild(i).gameObject.SendMessage("OnFracture", args, SendMessageOptions.DontRequireReceiver);
                }
            }

            result.SetResult(rootGO, operation.Result.EntireMeshBounds);
        }
    }
}