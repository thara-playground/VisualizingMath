#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    StructuredBuffer<float4x4> _Matrics;
#endif

float _Step;

void ConfigureProcedural () {
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    unity_ObjectToWorld = _Matrics[unity_InstanceID];
#endif
}

// connect it to the graph for using this hlsl from other shaders
void ShaderGraphFunction_float (float3 In, out float3 Out) {
	Out = In;
}

void ShaderGraphFunction_half (half3 In, out half3 Out) {
	Out = In;
}