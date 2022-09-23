using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;

internal class OvrComputeBufferPool : System.IDisposable
{
    // max active avatars, plus 4 extras(can go over by a couple for 1-2 frames sometimes)
    private const int BUFFER_SIZE = 36;
    // currently using enabled for both morph target and skinning calls.
    private const int BUFFER_SIZE_ENABLED = BUFFER_SIZE * 2;
    // 3 "should" be enough, use 4 just in case, might be needed in Editor
    private const int NUM_BUFFERS = 4;

    private const int VECTOR4_SIZE_BYTES = sizeof(float) * 4;
    private const int BYTES_PER_MATRIX = sizeof(float) * 16;

    // Our glb encodes joint indices in 8 bits currently.
    // Currently we use 134 bones. Its always creeping up though.
    // Leave some extra for future. This number can't be increased beyond 254 though
    // glb reserves some values, so only get 254 max in 8 bits.
    internal const int MaxJoints = 160;

    internal const int JointDataSize = 2 * BYTES_PER_MATRIX;
    [StructLayout(LayoutKind.Explicit, Size = JointDataSize)]
    internal struct JointData
    {
        [FieldOffset(0)] public Matrix4x4 transform;
        [FieldOffset(BYTES_PER_MATRIX)] public Matrix4x4 normalTransform;
    }

    internal OvrComputeBufferPool()
    {
        // struct must be multiple of alignment. 256 bytes will work for every platform,
        // add padding as needed. Unity has some api's you can use to avoid the padding,
        // but it seems to require either readonly, or making an additional copy of the data.
        _jointBuffer = new ComputeBuffer(BUFFER_SIZE * MaxJoints * NUM_BUFFERS, JointDataSize, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool isMainThread)
    {
        _jointBuffer.Release();
    }

    ~OvrComputeBufferPool()
    {
        Dispose(false);
    }

    public void StartFrameJoints()
    {
        var data = _jointBuffer.BeginWrite<JointData>(_currentJointBuffer * MaxJoints * BUFFER_SIZE, MaxJoints * BUFFER_SIZE);
        unsafe
        {
            _jointMappedData = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(data);
        }
        _jointNumberWritten = 0;
    }

    public void EndFrameJoints()
    {
        _jointBuffer.EndWrite<JointData>(_jointNumberWritten * MaxJoints);
        _currentJointBuffer = (_currentJointBuffer + 1) % NUM_BUFFERS;
    }

    public struct EntryJoints
    {
        public IntPtr Data;
        public int JointOffset;
    }

    public EntryJoints GetNextEntryJoints()
    {
        Debug.Assert(_jointNumberWritten < BUFFER_SIZE);
        EntryJoints result;
        result.JointOffset = _currentJointBuffer * MaxJoints * BUFFER_SIZE + (_jointNumberWritten * MaxJoints);
        unsafe
        {
            var jointSet = ((JointData*)_jointMappedData) + _jointNumberWritten * MaxJoints;
            result.Data = (IntPtr)jointSet;
        }
        ++_jointNumberWritten;
        return result;
    }

    internal ComputeBuffer GetJointBuffer()
    {
        return _jointBuffer;
    }

    private readonly ComputeBuffer _jointBuffer;

    //Note, this is only valid between StartFrame/EndFrame
    private unsafe void* _jointMappedData;

    int _currentJointBuffer = 0;
    int _jointNumberWritten;
}
