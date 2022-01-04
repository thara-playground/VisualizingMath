
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;
using quaternion = Unity.Mathematics.quaternion;
using Random = UnityEngine.Random;

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
        public NativeArray<float3x4> matrices;

        public void Execute (int i) {
            FractalPart parent = parents[i / 5];
            FractalPart part = parts[i];
            part.spinAngle += spinAngleDelta;

            float3 upAxis = mul(mul(parent.worldRotation, part.rotation), up());
            float3 sagAxis = cross(up(), upAxis);

            float sagMagnitude = length(sagAxis);
            quaternion baseRotation;
            if (0f < sagMagnitude) {
                sagAxis /= sagMagnitude;
                quaternion sagRotation = quaternion.AxisAngle(sagAxis, PI * 0.25f * sagMagnitude);
                baseRotation = mul(sagRotation, parent.worldRotation);
            } else {
                baseRotation = parent.worldRotation;
            }

            part.worldRotation = mul(baseRotation,
                mul(part.rotation, quaternion.RotateY(part.spinAngle)));
            part.worldPosition =
                parent.worldPosition +
                mul(part.worldRotation, float3(0f, 1.5f * scale, 0f));
            parts[i] = part;

            float3x3 r = float3x3(part.worldRotation) * scale;
            matrices[i] = float3x4(r.c0, r.c1, r.c2, part.worldPosition);
        }
    }

    struct FractalPart {
        public float3 worldPosition;
        public Quaternion rotation, worldRotation;
        public float spinAngle;
    }

    [SerializeField, Range(3, 8)]
    int depth = 4;

    [SerializeField]
    Mesh mesh, leafMesh;

    [SerializeField]
    Material material;

    [SerializeField]
    Gradient gradientA, gradientB;

    [SerializeField]
    Color leafColorA, leafColorB;

    NativeArray<FractalPart>[] parts;

    NativeArray<float3x4>[] matrics;

    ComputeBuffer[] matricesBuffers;

    static Quaternion[] rotations = {
        quaternion.identity,
        quaternion.RotateZ(-0.5f * PI), quaternion.RotateZ(0.5f * PI),
        quaternion.RotateX(0.5f * PI), quaternion.RotateX(-0.5f * PI)
    };

    static readonly int 
        colorAId = Shader.PropertyToID("_ColorA"),
        colorBId = Shader.PropertyToID("_ColorB"),
        matricesId = Shader.PropertyToID("_Matrices"),
        sequenceNumbersId = Shader.PropertyToID("_SequenceNumbers");

    static MaterialPropertyBlock propertyBlock;

    Vector4[] sequenceNumbers;

    void OnEnable ()
    {
        parts = new NativeArray<FractalPart>[depth];
        matrics = new NativeArray<float3x4>[depth];
        matricesBuffers = new ComputeBuffer[depth];
        sequenceNumbers = new Vector4[depth];
        int stride = 12 * 4;
        for (int i = 0, length = 1; i < parts.Length; i++, length *= 5) {
            parts[i] = new NativeArray<FractalPart>(length, Allocator.Persistent);
            matrics[i] = new NativeArray<float3x4>(length, Allocator.Persistent);
            matricesBuffers[i] = new ComputeBuffer(length, stride);
            sequenceNumbers[i] = new Vector4(Random.value, Random.value, Random.value, Random.value);
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
        sequenceNumbers = null;
    }

    void OnValidate()
    {
        if (parts != null && enabled) {
            OnDisable();
            OnEnable();
        }
    }

    FractalPart CreatePart(int childIndex) => new FractalPart {
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
        float3x3 r = float3x3(rootPart.worldRotation) * objectScale;
        matrics[0][0] = float3x4(r.c0, r.c1, r.c2, rootPart.worldPosition);

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
            }.ScheduleParallel(parts[li].Length, 5, jobHandle);
        }
        jobHandle.Complete();

        var bounds = new Bounds(rootPart.worldPosition, 3f * objectScale * Vector3.one);
        int leafIndex = matricesBuffers.Length - 1;
        for (int i = 0; i < matricesBuffers.Length; i++) {
            ComputeBuffer buffer = matricesBuffers[i];
            buffer.SetData(matrics[i]);
            Color colorA, colorB;
            Mesh instanceMesh;
            if (i == leafIndex) {
                colorA = leafColorA;
                colorB = leafColorB;
                instanceMesh = leafMesh;
            } else {
                float gradientInterpolator = i / (matricesBuffers.Length - 1f);
                colorA = gradientA.Evaluate(gradientInterpolator);
                colorB = gradientB.Evaluate(gradientInterpolator);
                instanceMesh = mesh;
            }
            propertyBlock.SetColor(colorAId, colorA);
            propertyBlock.SetColor(colorBId, colorB);
            propertyBlock.SetBuffer(matricesId, buffer);
            propertyBlock.SetVector(sequenceNumbersId, sequenceNumbers[i]);
            Graphics.DrawMeshInstancedProcedural(instanceMesh, 0, material, bounds, buffer.count, propertyBlock);
        }
    }
}
