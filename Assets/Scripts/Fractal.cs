
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;
using float4x4 = Unity.Mathematics.float4x4;
using quaternion = Unity.Mathematics.quaternion;

public class Fractal : MonoBehaviour
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    struct UpdateFractalLevelJob: IJobFor {
        public float spinAngleDelta;
        public float scale;

        [ReadOnly]
        public NativeArray<FractalPart> parents;

        public NativeArray<FractalPart> parts;

        [WriteOnly]
        public NativeArray<Matrix4x4> matrices;

        public void Execute (int i) {
            FractalPart parent = parents[i / 5];
            FractalPart part = parts[i];
            part.spinAngle += parent.spinAngle;
            part.worldRotation = mul(parent.worldRotation,
                mul(part.rotation, quaternion.RotateY(part.spinAngle)));
            part.worldPosition =
                (float3) parent.worldPosition +
                mul(parent.worldRotation, 1.5f * scale * part.direction);
            parts[i] = part;
            matrices[i] = Matrix4x4.TRS(
                part.worldPosition, part.worldRotation, float3(scale)
            );
        }
    }

    struct FractalPart {
        public Vector3 direction, worldPosition;
        public Quaternion rotation, worldRotation;
        public float spinAngle;
    }

    [SerializeField, Range(1, 8)]
    int depth = 4;

    [SerializeField]
    Mesh mesh;

    [SerializeField]
    Material material;

    NativeArray<FractalPart>[] parts;

    NativeArray<Matrix4x4>[] matrics;

    ComputeBuffer[] matricesBuffers;

    static Vector3[] directions = {
        up(), right(), left(), forward(), back()
    };

    static Quaternion[] rotations = {
        quaternion.identity,
        quaternion.RotateZ(-0.5f * PI), quaternion.RotateZ(0.5f * PI),
        quaternion.RotateX(0.5f * PI), quaternion.RotateX(-0.5f * PI)
    };

    static readonly int matricesId = Shader.PropertyToID("_Matrices");

    static MaterialPropertyBlock propertyBlock;

    void OnEnable ()
    {
        parts = new NativeArray<FractalPart>[depth];
        matrics = new NativeArray<Matrix4x4>[depth];
        matricesBuffers = new ComputeBuffer[depth];
        int stride = 16 * 4;
        for (int i = 0, length = 1; i < parts.Length; i++, length *= 5) {
            parts[i] = new NativeArray<FractalPart>(length, Allocator.Persistent);
            matrics[i] = new NativeArray<Matrix4x4>(length, Allocator.Persistent);
            matricesBuffers[i] = new ComputeBuffer(length, stride);
        }

        parts[0][0] = CreatePart(0);
        for (int li = 1; li < parts.Length; li++) {
            NativeArray<FractalPart> levelParts = parts[li];
            for (int fpi = 0; fpi < levelParts.Length; fpi += 5) {
                for (int ci = 0; ci < 5; ci++) {
                    levelParts[fpi + ci] = CreatePart(ci);
                }
            }
        }

        propertyBlock ??= new MaterialPropertyBlock();
    }

    void OnDisable ()
    {
        for (int i = 0; i < matricesBuffers.Length; i++) {
            matricesBuffers[i].Release();
            parts[i].Dispose();
            matrics[i].Dispose();
        }
        parts = null;
        matrics = null;
        matricesBuffers = null;
    }

    void OnValidate()
    {
        if (parts != null && enabled) {
            OnDisable();
            OnEnable();
        }
    }

    FractalPart CreatePart(int childIndex) => new FractalPart {
        direction = directions[childIndex],
        rotation = rotations[childIndex],
    };

    void Update()
    {
        float spinAngleDelta = 0.125f * PI * Time.deltaTime;

        FractalPart rootPart = parts[0][0];
        rootPart.spinAngle += spinAngleDelta;
        rootPart.worldRotation = mul(transform.rotation, mul(rootPart.rotation, quaternion.RotateY(rootPart.spinAngle)));
        rootPart.worldPosition = transform.position;
        parts[0][0] = rootPart;
        float objectScale = transform.lossyScale.x;
        matrics[0][0] = Matrix4x4.TRS(
            rootPart.worldPosition, rootPart.worldRotation, float3(objectScale)
        );

        float scale = objectScale;
        JobHandle jobHandle = default;
        for (int li = 1; li < parts.Length; li++) {
            scale *= 0.5f;
            jobHandle = new UpdateFractalLevelJob {
                spinAngleDelta = spinAngleDelta,
                scale = scale,
                parents = parts[li - 1],
                parts = parts[li],
                matrices = matrics[li]
            }.Schedule(parts[li].Length, jobHandle);
        }
        jobHandle.Complete();

        var bounds = new Bounds(rootPart.worldPosition, 3f * objectScale * Vector3.one);
        for (int i = 0; i < matricesBuffers.Length; i++) {
            ComputeBuffer buffer = matricesBuffers[i];
            buffer.SetData(matrics[i]);
            propertyBlock.SetBuffer(matricesId, buffer);
            Graphics.DrawMeshInstancedProcedural(mesh, 0, material, bounds, buffer.count, propertyBlock);
        }
    }
}
