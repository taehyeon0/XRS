// Check for differences in update vs current state and ignore if they match
//#define OVR_GPUSKINNING_DIFFERENCE_CHECK

using Oculus.Avatar2;

using System;
using System.Collections.Generic;

using Unity.Collections;

using UnityEngine;
using UnityEngine.Profiling;

namespace Oculus.Skinning.GpuSkinning
{
    internal class OvrGpuCombinerDrawCall
    {
        private const string logScope = "OvrGpuCombinerDrawCall";
        internal OvrGpuCombinerDrawCall(
            Shader combineShader,
            OvrExpandableTextureArray morphTargetsSourceTex,
            Vector4[] ranges,
            bool hasTangents,
            bool useSNorm10)
        {
            _morphTargetsSourceTex = morphTargetsSourceTex;
            _combineMaterial = new Material(combineShader);
            if (hasTangents)
            {
                _combineMaterial.EnableKeyword(OVR_HAS_TANGENTS);
            }
            if (useSNorm10)
            {
                _combineMaterial.EnableKeyword(OVR_MORPH_10_10_10_2);
            }
            _combineMaterial.SetVectorArray(MORPH_TARGET_RANGES_PROP, ranges);

            _mesh = new Mesh
            {
                vertices = Array.Empty<Vector3>(),
                uv = Array.Empty<Vector2>(),
                colors = Array.Empty<Color>(),
                triangles = Array.Empty<int>()
            };

            _meshLayout = new OvrFreeListBufferTracker(MAX_QUADS);
            _handleToBlockData = new Dictionary<OvrSkinningTypes.Handle, BlockData>();

            _morphTargetsSourceTex.ArrayResized += MorphTargetsSourceTexArrayResized;
        }

        private void MorphTargetsSourceTexArrayResized(object sender, Texture2DArray newArray)
        {
            SetMorphSourceTexture(newArray);
        }

        internal void Destroy()
        {
            _morphTargetsSourceTex.ArrayResized -= MorphTargetsSourceTexArrayResized;

            if (_combineMaterial)
            {
                Material.Destroy(_combineMaterial);
            }

            if (_weightsList.IsCreated) { _weightsList.Dispose(); }

            _weightsBuffer?.Release();
        }

        // The shapesRect is specified in texels
        internal OvrSkinningTypes.Handle AddMorphTargetsToMesh(
            RectInt texelRectInSource,
            int sourceTexSlice,
            int sourceTexWidth,
            int sourceTexHeight,
            RectInt texelRectInOutput,
            int outputTexWidth,
            int outputTexHeight,
            int numMorphTargets)
        {
            OvrSkinningTypes.Handle layoutHandle = _meshLayout.TrackBlock(numMorphTargets);

            if (!layoutHandle.IsValid())
            {
                return layoutHandle;
            }

            OvrFreeListBufferTracker.LayoutResult quadsLayout = _meshLayout.GetLayoutInBufferForBlock(layoutHandle);

            // Create Quads if needed
            int quadIndex = quadsLayout.startIndex;
            int vertStartIndex = quadIndex * NUM_VERTS_PER_QUAD;
            int blockIndex = layoutHandle.GetValue();

            if (vertStartIndex >= _mesh.vertexCount)
            {
                OvrCombinedMorphTargetsQuads.ExpandMeshToFitQuads(_mesh, numMorphTargets);
            }

            OvrCombinedMorphTargetsQuads.UpdateQuadsInMesh(
                vertStartIndex,
                blockIndex,
                quadIndex,
                texelRectInOutput,
                outputTexWidth,
                outputTexHeight,
                texelRectInSource,
                sourceTexSlice,
                sourceTexWidth,
                sourceTexHeight,
                numMorphTargets,
                _mesh);

            // Expand compute buffers and lists if needed
            int newNumBlocks = blockIndex + 1;
            int currentWeightsLength = _weightsBuffer?.count ?? 0;
            int newNumWeights = currentWeightsLength + numMorphTargets;

            var oldWeightsBuffer = _weightsBuffer;
            var oldWeightsList = _weightsList;

            _weightsBuffer = new ComputeBuffer(newNumWeights, sizeof(float));
            _weightsList = new NativeArray<float>(newNumWeights, Allocator.Persistent, NativeArrayOptions.ClearMemory);


            // Copy old weights to new weightsList
            if (oldWeightsList.IsCreated)
            {
                NativeArray<float>.Copy(oldWeightsList, _weightsList);
                //oldWeightsList.CopyTo(_weightsList.GetSubArray(0, oldWeightsList.Length));
                oldWeightsList.Dispose();
            }
            // Copy weights list into new buffer
            _weightsBuffer.SetData(_weightsList, 0, 0, newNumWeights);


            _combineMaterial.SetFloat(BLOCK_ENABLED_PROP, 1.0f);

            SetBuffersInMaterial();

            if (oldWeightsBuffer != null)
            {
                oldWeightsBuffer.Release();
            }

            // Add new mapping of handle to block data
            _handleToBlockData[layoutHandle] = new BlockData
            {
                blockIndex = blockIndex,
                indexInWeightsBuffer = currentWeightsLength,
                numMorphTargets = numMorphTargets
            };

            return layoutHandle;
        }

        internal void RemoveMorphTargetBlock(OvrSkinningTypes.Handle handle)
        {
            _meshLayout.FreeBlock(handle);
        }

        internal NativeSlice<float> GetMorphWeightsBuffer(OvrSkinningTypes.Handle handle)
        {
            Debug.Assert(_weightsBuffer != null && _weightsList.IsCreated);
            if (_handleToBlockData.TryGetValue(handle, out BlockData blockData))
            {
                return _weightsList.Slice(blockData.indexInWeightsBuffer, blockData.numMorphTargets);
            }
            return default;
        }

        internal bool MorphWeightsBufferUpdateComplete(OvrSkinningTypes.Handle handle)
        {
            bool drawUpdateNeeded = false;
            Debug.Assert(_weightsBuffer != null && _weightsList.IsCreated);
            if (_weightsBuffer != null && _handleToBlockData.TryGetValue(handle, out BlockData dataForThisBlock))
            {
                MarkBlockUpdated(in dataForThisBlock);
                drawUpdateNeeded = true;
            }
            return drawUpdateNeeded;
        }

        internal void Draw()
        {
            if (_areAnyBlocksEnabled)
            {
                ForceDraw();
            }
        }

        internal void ForceDraw()
        {
            Profiler.BeginSample("OvrGpuCombinerDrawCall::ForceDraw");


            // Copy from block enabled array to compute buffer

            // Don't care about matrices as the shader used should handle clip space
            // conversions without matrices (due to how quads set up)
            bool didSetPass = _combineMaterial.SetPass(0);
            Debug.Assert(didSetPass);
            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);

            _combineMaterial.SetFloat(BLOCK_ENABLED_PROP, 0.0f);

            // Reset booleans and mark all blocks as disabled for next frame
            _areAnyBlocksEnabled = false;

            Profiler.EndSample();
        }

        internal bool CanFit(int numMorphTargets)
        {
            return _meshLayout.CanFit(numMorphTargets);
        }

        private void SetMorphSourceTexture(Texture2DArray morphTargetsSourceTex)
        {
            _combineMaterial.SetTexture(MORPH_TARGETS_SOURCE_TEX_PROP, morphTargetsSourceTex);
        }

        private void SetBuffersInMaterial()
        {
            _combineMaterial.SetBuffer(MORPH_TARGET_WEIGHTS_PROP, _weightsBuffer);
        }

        private void MarkBlockUpdated(in BlockData block)
        {
            Debug.Assert(block.blockIndex == 0);
            Debug.Assert(_weightsList.Length == block.numMorphTargets);

            int blockOffset = block.indexInWeightsBuffer;
            _weightsBuffer.SetData(_weightsList, 0, blockOffset, block.numMorphTargets);
            _combineMaterial.SetFloat(BLOCK_ENABLED_PROP, 1.0f);
            _areAnyBlocksEnabled = true;
        }

        // Using `_BlockRangeMessageBuilder` causes a GC.Alloc in Mono - go figure?
        private static readonly OvrAvatarLog.AssertLessThanMessageBuilder<int> _CacheBlockRangeMessageBuilder
            = _BlockRangeMessageBuilder;
        private static string _BlockRangeMessageBuilder(in int blkIdx, in int len)
            => $"BlockIndex {blkIdx} out of range of _blockEnabled[{len}] list";

        // Unity is supposed to provide a resizable NativeList type, which would be clutch
        // though I followed the instructions to install the package and it wasn't visible from here?
        private NativeArray<float> _weightsList;

        private ComputeBuffer _weightsBuffer = null;

        private readonly Mesh _mesh;
        private readonly OvrFreeListBufferTracker _meshLayout;

        private readonly Material _combineMaterial;

        private bool _areAnyBlocksEnabled = false;

        private struct BlockData
        {
            public int blockIndex;
            public int indexInWeightsBuffer;
            public int numMorphTargets;
        }

        private readonly Dictionary<OvrSkinningTypes.Handle, BlockData> _handleToBlockData;

        private readonly OvrExpandableTextureArray _morphTargetsSourceTex;

        private const int NUM_VERTS_PER_QUAD = 4;
        private const int MAX_QUADS = ushort.MaxValue / NUM_VERTS_PER_QUAD;

        private const string OVR_HAS_TANGENTS = "OVR_HAS_TANGENTS";
        private const string OVR_MORPH_10_10_10_2 = "OVR_MORPH_10_10_10_2";

        private static readonly int MORPH_TARGETS_SOURCE_TEX_PROP = Shader.PropertyToID("u_MorphTargetSourceTex");
        private static readonly int MORPH_TARGET_WEIGHTS_PROP = Shader.PropertyToID("u_Weights");
        private static readonly int BLOCK_ENABLED_PROP = Shader.PropertyToID("u_BlockEnabled");
        private static readonly int MORPH_TARGET_RANGES_PROP = Shader.PropertyToID("u_MorphTargetRanges");

    }
}
