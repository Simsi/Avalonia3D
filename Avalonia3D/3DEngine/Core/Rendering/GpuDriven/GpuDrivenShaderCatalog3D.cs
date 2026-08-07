using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Core.Rendering.GpuDriven;

/// <summary>
/// Canonical shader identities and WGSL sources for the GPU-driven path. Explicit backends resolve
/// modules by identity; the RHI reflection data is generated from the same catalog so a shader and
/// its bind-group contract cannot silently diverge.
/// </summary>
internal static class GpuDrivenShaderCatalog3D
{
    public const string CullMeshlets = "avalonia3d/gpu-driven/cull-meshlets.wgsl@2";
    public const string BuildClusters = "avalonia3d/gpu-driven/build-clusters.wgsl@2";
    public const string SimulateParticles = "avalonia3d/gpu-driven/simulate-particles.wgsl@2";
    public const string ForwardVertex = "avalonia3d/gpu-driven/forward.vert.wgsl@2";
    public const string ForwardFragment = "avalonia3d/gpu-driven/forward.frag.wgsl@2";
    public const string ParticleVertex = "avalonia3d/gpu-driven/particle.vert.wgsl@2";
    public const string ParticleFragment = "avalonia3d/gpu-driven/particle.frag.wgsl@2";
    public const string ToneMapVertex = "avalonia3d/gpu-driven/tonemap.vert.wgsl@2";
    public const string ToneMapFragment = "avalonia3d/gpu-driven/tonemap.frag.wgsl@2";

    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CullMeshlets] = """
            struct FrameConstants {
                view: mat4x4<f32>,
                projection: mat4x4<f32>,
                viewProjection: mat4x4<f32>,
                cameraPositionTime: vec4<f32>,
                viewportAndInverse: vec4<f32>,
                counts: vec4<f32>,
                lightCounts: vec4<f32>,
                clusterDimensions: vec4<f32>,
                featureFlags: vec4<f32>,
                timing: vec4<f32>,
            }
            struct SceneObject {
                model: mat4x4<f32>,
                boundingSphere: vec4<f32>,
                meshIndex: u32,
                materialIndex: u32,
                flags: u32,
                skinPaletteOffset: u32,
            }
            struct MeshRecord {
                vertexCount: u32,
                indexCount: u32,
                meshletOffset: u32,
                meshletCount: u32,
                indexElementSize: u32,
                vertexStride: u32,
                indirectBase: u32,
                counterIndex: u32,
            }
            struct MeshletRecord {
                boundingSphere: vec4<f32>,
                normalCone: vec4<f32>,
                vertexOffset: u32,
                vertexCount: u32,
                triangleOffset: u32,
                triangleCount: u32,
                meshIndex: u32,
                reserved0: u32,
                reserved1: u32,
                reserved2: u32,
            }
            struct DrawIndexedIndirect {
                indexCount: u32,
                instanceCount: u32,
                firstIndex: u32,
                baseVertex: i32,
                firstInstance: u32,
            }
            @group(0) @binding(0) var<uniform> frame: FrameConstants;
            @group(0) @binding(1) var<storage, read> objects: array<SceneObject>;
            @group(0) @binding(2) var<storage, read> meshes: array<MeshRecord>;
            @group(0) @binding(3) var<storage, read> meshlets: array<MeshletRecord>;
            @group(0) @binding(4) var<storage, read> materials: array<vec4<f32>>;
            @group(0) @binding(5) var<storage, read_write> visibleMeshlets: array<u32>;
            @group(0) @binding(6) var<storage, read_write> indirectCommands: array<DrawIndexedIndirect>;
            @group(0) @binding(7) var<storage, read_write> indirectCounters: array<atomic<u32>>;

            fn max_model_scale(model: mat4x4<f32>) -> f32 {
                return max(length(model[0].xyz), max(length(model[1].xyz), length(model[2].xyz)));
            }

            fn sphere_in_frustum(center: vec3<f32>, radius: f32) -> bool {
                let clip = frame.viewProjection * vec4<f32>(center, 1.0);
                if (clip.w <= 0.0) { return false; }
                let rx = abs(frame.projection[0][0]) * radius;
                let ry = abs(frame.projection[1][1]) * radius;
                return clip.x >= -clip.w - rx && clip.x <= clip.w + rx &&
                       clip.y >= -clip.w - ry && clip.y <= clip.w + ry &&
                       clip.z >= -radius && clip.z <= clip.w + radius;
            }

            @compute @workgroup_size(128)
            fn main(@builtin(global_invocation_id) id: vec3<u32>) {
                let objectIndex = id.x;
                if (objectIndex >= u32(frame.counts.x)) { return; }
                let object = objects[objectIndex];
                if ((object.flags & 1u) == 0u) { return; }
                if (!sphere_in_frustum(object.boundingSphere.xyz, object.boundingSphere.w)) { return; }
                let mesh = meshes[object.meshIndex];
                let scale = max_model_scale(object.model);
                for (var localMeshlet = 0u; localMeshlet < mesh.meshletCount; localMeshlet++) {
                    let meshletIndex = mesh.meshletOffset + localMeshlet;
                    let meshlet = meshlets[meshletIndex];
                    let worldCenter = (object.model * vec4<f32>(meshlet.boundingSphere.xyz, 1.0)).xyz;
                    let worldRadius = meshlet.boundingSphere.w * scale;
                    if (!sphere_in_frustum(worldCenter, worldRadius)) { continue; }
                    if (frame.featureFlags.y > 0.5) {
                        let worldAxis = normalize((object.model * vec4<f32>(meshlet.normalCone.xyz, 0.0)).xyz);
                        let viewDirection = normalize(frame.cameraPositionTime.xyz - worldCenter);
                        if (dot(worldAxis, viewDirection) <= meshlet.normalCone.w) { continue; }
                    }
                    let compactIndex = atomicAdd(&indirectCounters[mesh.counterIndex], 1u);
                    let commandIndex = mesh.indirectBase + compactIndex;
                    if (commandIndex >= arrayLength(&indirectCommands)) { continue; }
                    visibleMeshlets[commandIndex] = meshletIndex;
                    indirectCommands[commandIndex] = DrawIndexedIndirect(
                        meshlet.triangleCount * 3u,
                        1u,
                        meshlet.triangleOffset * 3u,
                        0,
                        objectIndex);
                }
            }
            """,
        [BuildClusters] = """
            struct FrameConstants {
                view: mat4x4<f32>, projection: mat4x4<f32>, viewProjection: mat4x4<f32>,
                cameraPositionTime: vec4<f32>, viewportAndInverse: vec4<f32>, counts: vec4<f32>,
                lightCounts: vec4<f32>, clusterDimensions: vec4<f32>, featureFlags: vec4<f32>, timing: vec4<f32>,
            }
            struct DirectionalLight { directionIntensity: vec4<f32>, colorEnabled: vec4<f32>  }
            struct PointLight { positionRange: vec4<f32>, colorIntensity: vec4<f32>  }
            struct SpotLight { positionRange: vec4<f32>, directionInnerCos: vec4<f32>, colorIntensity: vec4<f32>, outerCosEnabled: vec4<f32>  }
            @group(0) @binding(0) var<uniform> frame: FrameConstants;
            @group(1) @binding(0) var<storage, read> directionalLights: array<DirectionalLight>;
            @group(1) @binding(1) var<storage, read> pointLights: array<PointLight>;
            @group(1) @binding(2) var<storage, read> spotLights: array<SpotLight>;
            @group(1) @binding(3) var<storage, read_write> clusterGrid: array<vec2<u32>>;
            @group(1) @binding(4) var<storage, read_write> clusterLightIndices: array<u32>;

            @compute @workgroup_size(64)
            fn main(@builtin(global_invocation_id) id: vec3<u32>) {
                let clusterIndex = id.x;
                let clusterCount = u32(frame.clusterDimensions.w);
                if (clusterIndex >= clusterCount) { return; }
                let maximum = u32(frame.lightCounts.w);
                let offset = clusterIndex * maximum;
                var count = 0u;
                let pointCount = u32(frame.lightCounts.y);
                for (var i = 0u; i < pointCount && count < maximum; i++) {
                    if (pointLights[i].colorIntensity.w > 0.0) {
                        clusterLightIndices[offset + count] = i;
                        count++;
                    }
                }
                let spotCount = u32(frame.lightCounts.z);
                for (var i = 0u; i < spotCount && count < maximum; i++) {
                    if (spotLights[i].outerCosEnabled.y > 0.5) {
                        clusterLightIndices[offset + count] = pointCount + i;
                        count++;
                    }
                }
                clusterGrid[clusterIndex] = vec2<u32>(offset, count);
            }
            """,
        [SimulateParticles] = """
            struct FrameConstants {
                view: mat4x4<f32>, projection: mat4x4<f32>, viewProjection: mat4x4<f32>,
                cameraPositionTime: vec4<f32>, viewportAndInverse: vec4<f32>, counts: vec4<f32>,
                lightCounts: vec4<f32>, clusterDimensions: vec4<f32>, featureFlags: vec4<f32>, timing: vec4<f32>,
            }
            struct Emitter {
                model: mat4x4<f32>, directionEmissionRate: vec4<f32>, gravityLifetime: vec4<f32>,
                sizeSpeedSpread: vec4<f32>, startColor: vec4<f32>, endColor: vec4<f32>,
                stateOffset: u32, capacity: u32, flags: u32, randomSeed: u32,
            }
            struct ParticleState {
                positionAge: vec4<f32>, velocityLifetime: vec4<f32>, color: vec4<f32>, sizeRotationFlags: vec4<f32>,
            }
            @group(0) @binding(0) var<uniform> frame: FrameConstants;
            @group(2) @binding(0) var<storage, read> emitters: array<Emitter>;
            @group(2) @binding(1) var<storage, read> sourceParticles: array<ParticleState>;
            @group(2) @binding(2) var<storage, read_write> destinationParticles: array<ParticleState>;
            @group(2) @binding(3) var<storage, read_write> particleCounters: array<atomic<u32>>;
            @group(2) @binding(4) var<storage, read_write> particleIndirect: array<atomic<u32>>;

            fn hash(value: u32) -> f32 {
                var x = value;
                x = ((x >> 16u) ^ x) * 0x45d9f3bu;
                x = ((x >> 16u) ^ x) * 0x45d9f3bu;
                x = (x >> 16u) ^ x;
                return f32(x & 0x00ffffffu) / 16777215.0;
            }

            @compute @workgroup_size(128)
            fn main(@builtin(global_invocation_id) id: vec3<u32>) {
                let particleIndex = id.x;
                if (particleIndex >= arrayLength(&sourceParticles)) { return; }
                var emitterIndex = 0xffffffffu;
                var localIndex = 0u;
                for (var i = 0u; i < arrayLength(&emitters); i++) {
                    let e = emitters[i];
                    if (particleIndex >= e.stateOffset && particleIndex < e.stateOffset + e.capacity) {
                        emitterIndex = i;
                        localIndex = particleIndex - e.stateOffset;
                        break;
                    }
                }
                if (emitterIndex == 0xffffffffu) { return; }
                let emitter = emitters[emitterIndex];
                atomicStore(&particleIndirect[emitterIndex * 4u + 0u], 6u);
                atomicStore(&particleIndirect[emitterIndex * 4u + 2u], 0u);
                atomicStore(&particleIndirect[emitterIndex * 4u + 3u], emitter.stateOffset);
                var particle = sourceParticles[particleIndex];
                let dt = clamp(frame.timing.x, 0.0, 0.1);
                var alive = particle.sizeRotationFlags.w > 0.5;
                if (alive) {
                    particle.positionAge.xyz += particle.velocityLifetime.xyz * dt;
                    particle.velocityLifetime.xyz += emitter.gravityLifetime.xyz * dt;
                    particle.positionAge.w += dt;
                    alive = particle.positionAge.w < particle.velocityLifetime.w;
                }
                if (!alive && (emitter.flags & 1u) != 0u) {
                    let probability = clamp(emitter.directionEmissionRate.w * dt / max(1.0, f32(emitter.capacity)), 0.0, 1.0);
                    let seed = emitter.randomSeed ^ localIndex ^ u32(frame.cameraPositionTime.w * 1000.0);
                    if (hash(seed) <= probability) {
                        let spread = emitter.sizeSpeedSpread.w;
                        let jitter = vec3<f32>(hash(seed + 1u) - 0.5, hash(seed + 2u) - 0.5, hash(seed + 3u) - 0.5) * spread;
                        let direction = normalize(emitter.directionEmissionRate.xyz + jitter);
                        particle.positionAge = vec4<f32>((emitter.model * vec4<f32>(0.0, 0.0, 0.0, 1.0)).xyz, 0.0);
                        particle.velocityLifetime = vec4<f32>(direction * emitter.sizeSpeedSpread.z, emitter.gravityLifetime.w);
                        particle.color = emitter.startColor;
                        particle.sizeRotationFlags = vec4<f32>(emitter.sizeSpeedSpread.x, emitter.sizeSpeedSpread.y, 0.0, 1.0);
                        alive = true;
                    }
                }
                if (!alive) { return; }
                let compactIndex = atomicAdd(&particleCounters[emitterIndex], 1u);
                if (compactIndex >= emitter.capacity) { return; }
                let outputIndex = emitter.stateOffset + compactIndex;
                let normalizedAge = clamp(particle.positionAge.w / max(0.0001, particle.velocityLifetime.w), 0.0, 1.0);
                particle.color = mix(emitter.startColor, emitter.endColor, normalizedAge);
                destinationParticles[outputIndex] = particle;
                atomicAdd(&particleIndirect[emitterIndex * 4u + 1u], 1u);
            }
            """,
        [ForwardVertex] = """
            struct FrameConstants {
                view: mat4x4<f32>, projection: mat4x4<f32>, viewProjection: mat4x4<f32>,
                cameraPositionTime: vec4<f32>, viewportAndInverse: vec4<f32>, counts: vec4<f32>,
                lightCounts: vec4<f32>, clusterDimensions: vec4<f32>, featureFlags: vec4<f32>, timing: vec4<f32>,
            }
            struct SceneObject {
                model: mat4x4<f32>, boundingSphere: vec4<f32>, meshIndex: u32, materialIndex: u32, flags: u32, skinPaletteOffset: u32,
            }
            struct VertexInput {
                @location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>,
                @location(3) tangent: vec4<f32>, @location(4) color: vec4<f32>, @location(5) materialSlot: f32,
                @location(6) boneIndices: vec4<f32>, @location(7) boneWeights: vec4<f32>,
            }
            struct VertexOutput {
                @builtin(position) position: vec4<f32>, @location(0) worldPosition: vec3<f32>, @location(1) worldNormal: vec3<f32>,
                @location(2) uv: vec2<f32>, @location(3) color: vec4<f32>, @location(4) @interpolate(flat) materialIndex: u32,
            }
            @group(0) @binding(0) var<uniform> frame: FrameConstants;
            @group(0) @binding(1) var<storage, read> objects: array<SceneObject>;
            @group(0) @binding(8) var<storage, read> skinMatrices: array<mat4x4<f32>>;

            @vertex fn main(input: VertexInput, @builtin(instance_index) instanceIndex: u32) -> VertexOutput {
                let object = objects[instanceIndex];
                var localPosition = vec4<f32>(input.position, 1.0);
                var localNormal = vec4<f32>(input.normal, 0.0);
                if ((object.flags & 4u) != 0u && object.skinPaletteOffset != 0xffffffffu) {
                    let indices = vec4<u32>(input.boneIndices);
                    let weights = input.boneWeights;
                    let skin = skinMatrices[object.skinPaletteOffset + indices.x] * weights.x +
                               skinMatrices[object.skinPaletteOffset + indices.y] * weights.y +
                               skinMatrices[object.skinPaletteOffset + indices.z] * weights.z +
                               skinMatrices[object.skinPaletteOffset + indices.w] * weights.w;
                    localPosition = skin * localPosition;
                    localNormal = skin * localNormal;
                }
                let worldPosition4 = object.model * localPosition;
                var output: VertexOutput;
                output.position = frame.viewProjection * worldPosition4;
                output.worldPosition = worldPosition4.xyz;
                output.worldNormal = normalize((object.model * localNormal).xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.materialIndex = object.materialIndex;
                return output;
            }
            """,
        [ForwardFragment] = """
            struct FrameConstants {
                view: mat4x4<f32>, projection: mat4x4<f32>, viewProjection: mat4x4<f32>,
                cameraPositionTime: vec4<f32>, viewportAndInverse: vec4<f32>, counts: vec4<f32>,
                lightCounts: vec4<f32>, clusterDimensions: vec4<f32>, featureFlags: vec4<f32>, timing: vec4<f32>,
            }
            struct Material { baseColor: vec4<f32>, emissiveMetallic: vec4<f32>, surfaceParameters: vec4<f32>, textureIndices: vec4<f32>  }
            struct DirectionalLight { directionIntensity: vec4<f32>, colorEnabled: vec4<f32>  }
            struct PointLight { positionRange: vec4<f32>, colorIntensity: vec4<f32>  }
            struct SpotLight { positionRange: vec4<f32>, directionInnerCos: vec4<f32>, colorIntensity: vec4<f32>, outerCosEnabled: vec4<f32>  }
            struct FragmentInput {
                @builtin(position) position: vec4<f32>, @location(0) worldPosition: vec3<f32>, @location(1) worldNormal: vec3<f32>,
                @location(2) uv: vec2<f32>, @location(3) color: vec4<f32>, @location(4) @interpolate(flat) materialIndex: u32,
            }
            @group(0) @binding(0) var<uniform> frame: FrameConstants;
            @group(0) @binding(4) var<storage, read> materials: array<Material>;
            @group(1) @binding(0) var<storage, read> directionalLights: array<DirectionalLight>;
            @group(1) @binding(1) var<storage, read> pointLights: array<PointLight>;
            @group(1) @binding(2) var<storage, read> spotLights: array<SpotLight>;
            @group(1) @binding(3) var<storage, read_write> clusterGrid: array<vec2<u32>>;
            @group(1) @binding(4) var<storage, read_write> clusterLightIndices: array<u32>;

            fn evaluate_light(n: vec3<f32>, v: vec3<f32>, l: vec3<f32>, radiance: vec3<f32>, base: vec3<f32>, metallic: f32, roughness: f32) -> vec3<f32> {
                let h = normalize(v + l);
                let ndotl = max(dot(n, l), 0.0);
                let ndoth = max(dot(n, h), 0.0);
                let specPower = mix(256.0, 4.0, clamp(roughness, 0.04, 1.0));
                let f0 = mix(vec3<f32>(0.04), base, metallic);
                let specular = f0 * pow(ndoth, specPower);
                let diffuse = base * (1.0 - metallic) / 3.14159265;
                return (diffuse + specular) * radiance * ndotl;
            }

            @fragment fn main(input: FragmentInput) -> @location(0) vec4<f32> {
                let material = materials[input.materialIndex];
                let base = material.baseColor.rgb * input.color.rgb;
                let metallic = clamp(material.emissiveMetallic.w, 0.0, 1.0);
                let roughness = clamp(material.surfaceParameters.x, 0.04, 1.0);
                let n = normalize(input.worldNormal);
                let v = normalize(frame.cameraPositionTime.xyz - input.worldPosition);
                var color = material.emissiveMetallic.rgb + base * 0.02;
                let directionalCount = u32(frame.lightCounts.x);
                for (var i = 0u; i < directionalCount; i++) {
                    let light = directionalLights[i];
                    if (light.colorEnabled.w > 0.5) {
                        color += evaluate_light(n, v, normalize(-light.directionIntensity.xyz), light.colorEnabled.rgb * light.directionIntensity.w, base, metallic, roughness);
                    }
                }
                if (frame.featureFlags.w > 0.5) {
                    let dims = vec3<u32>(frame.clusterDimensions.xyz);
                    let x = min(dims.x - 1u, u32(clamp(input.position.x * frame.viewportAndInverse.z, 0.0, 0.999999) * f32(dims.x)));
                    let y = min(dims.y - 1u, u32(clamp(input.position.y * frame.viewportAndInverse.w, 0.0, 0.999999) * f32(dims.y)));
                    let z = min(dims.z - 1u, u32(clamp(input.position.z, 0.0, 0.999999) * f32(dims.z)));
                    let clusterIndex = x + y * dims.x + z * dims.x * dims.y;
                    let grid = clusterGrid[clusterIndex];
                    let pointCount = u32(frame.lightCounts.y);
                    for (var i = 0u; i < grid.y; i++) {
                        let encoded = clusterLightIndices[grid.x + i];
                        if (encoded < pointCount) {
                            let light = pointLights[encoded];
                            let toLight = light.positionRange.xyz - input.worldPosition;
                            let distance = length(toLight);
                            let attenuation = pow(clamp(1.0 - distance / max(0.0001, light.positionRange.w), 0.0, 1.0), 2.0);
                            color += evaluate_light(n, v, normalize(toLight), light.colorIntensity.rgb * light.colorIntensity.w * attenuation, base, metallic, roughness);
                        } else {
                            let light = spotLights[encoded - pointCount];
                            let toLight = light.positionRange.xyz - input.worldPosition;
                            let distance = length(toLight);
                            let l = normalize(toLight);
                            let cone = smoothstep(light.outerCosEnabled.x, light.directionInnerCos.w, dot(normalize(-light.directionInnerCos.xyz), l));
                            let attenuation = cone * pow(clamp(1.0 - distance / max(0.0001, light.positionRange.w), 0.0, 1.0), 2.0);
                            color += evaluate_light(n, v, l, light.colorIntensity.rgb * light.colorIntensity.w * attenuation, base, metallic, roughness);
                        }
                    }
                }
                return vec4<f32>(color, material.baseColor.a * input.color.a);
            }
            """,
        [ParticleVertex] = """
            struct FrameConstants {
                view: mat4x4<f32>, projection: mat4x4<f32>, viewProjection: mat4x4<f32>,
                cameraPositionTime: vec4<f32>, viewportAndInverse: vec4<f32>, counts: vec4<f32>,
                lightCounts: vec4<f32>, clusterDimensions: vec4<f32>, featureFlags: vec4<f32>, timing: vec4<f32>,
            }
            struct ParticleState { positionAge: vec4<f32>, velocityLifetime: vec4<f32>, color: vec4<f32>, sizeRotationFlags: vec4<f32>  }
            struct ParticleVertexOutput { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32>, @location(1) color: vec4<f32>  }
            @group(0) @binding(0) var<uniform> frame: FrameConstants;
            @group(2) @binding(2) var<storage, read_write> particles: array<ParticleState>;
            @vertex fn main(@builtin(vertex_index) vertexIndex: u32, @builtin(instance_index) instanceIndex: u32) -> ParticleVertexOutput {
                let corners = array<vec2<f32>, 6>(
                    vec2<f32>(-1.0,-1.0), vec2<f32>(1.0,-1.0), vec2<f32>(1.0,1.0),
                    vec2<f32>(-1.0,-1.0), vec2<f32>(1.0,1.0), vec2<f32>(-1.0,1.0));
                let corner = corners[vertexIndex];
                let particle = particles[instanceIndex];
                let age = clamp(particle.positionAge.w / max(0.0001, particle.velocityLifetime.w), 0.0, 1.0);
                let size = mix(particle.sizeRotationFlags.x, particle.sizeRotationFlags.y, age);
                let right = normalize(frame.view[0].xyz);
                let up = normalize(frame.view[1].xyz);
                let world = particle.positionAge.xyz + (right * corner.x + up * corner.y) * size;
                var output: ParticleVertexOutput;
                output.position = frame.viewProjection * vec4<f32>(world, 1.0);
                output.uv = corner * 0.5 + vec2<f32>(0.5);
                output.color = particle.color;
                return output;
            }
            """,
        [ParticleFragment] = """
            struct Input { @location(0) uv: vec2<f32>, @location(1) color: vec4<f32>  }
            @fragment fn main(input: Input) -> @location(0) vec4<f32> {
                let radial = length(input.uv * 2.0 - vec2<f32>(1.0));
                let alpha = input.color.a * smoothstep(1.0, 0.65, radial);
                return vec4<f32>(input.color.rgb * alpha, alpha);
            }
            """,
        [ToneMapVertex] = """
            struct Output { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32>  }
            @vertex fn main(@builtin(vertex_index) vertexIndex: u32) -> Output {
                let positions = array<vec2<f32>, 3>(vec2<f32>(-1.0,-1.0), vec2<f32>(3.0,-1.0), vec2<f32>(-1.0,3.0));
                var output: Output;
                output.position = vec4<f32>(positions[vertexIndex], 0.0, 1.0);
                output.uv = output.position.xy * vec2<f32>(0.5, -0.5) + vec2<f32>(0.5);
                return output;
            }
            """,
        [ToneMapFragment] = """
            @group(3) @binding(0) var hdrTexture: texture_2d<f32>;
            @group(3) @binding(1) var linearSampler: sampler;
            fn aces(color: vec3<f32>) -> vec3<f32> {
                let a = 2.51; let b = 0.03; let c = 2.43; let d = 0.59; let e = 0.14;
                return clamp((color * (a * color + vec3<f32>(b))) / (color * (c * color + vec3<f32>(d)) + vec3<f32>(e)), vec3<f32>(0.0), vec3<f32>(1.0));
            }
            @fragment fn main(@location(0) uv: vec2<f32>) -> @location(0) vec4<f32> {
                let hdr = textureSample(hdrTexture, linearSampler, uv).rgb;
                let mapped = aces(hdr);
                return vec4<f32>(pow(mapped, vec3<f32>(1.0 / 2.2)), 1.0);
            }
            """
    };
    public static bool TryResolve(string sourceIdentity, out string source)
        => Sources.TryGetValue(sourceIdentity, out source!);

    public static string Resolve(string sourceIdentity)
        => TryResolve(sourceIdentity, out var source)
            ? source
            : throw new InvalidOperationException($"Unknown GPU-driven shader identity '{sourceIdentity}'.");

    public static RhiShaderModuleDescriptor3D CreateCullMeshlets()
        => Module("gpu-driven-cull-meshlets", CullMeshlets,
            B(0, 0, "frame", RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Compute),
            B(0, 1, "objects", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(0, 2, "meshes", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(0, 3, "meshlets", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(0, 4, "materials", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(0, 5, "visibleMeshlets", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
            B(0, 6, "indirectCommands", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
            B(0, 7, "indirectCounters", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute));

    public static RhiShaderModuleDescriptor3D CreateBuildClusters()
        => Module("gpu-driven-build-clusters", BuildClusters,
            B(0, 0, "frame", RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Compute),
            B(1, 0, "directionalLights", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(1, 1, "pointLights", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(1, 2, "spotLights", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(1, 3, "clusterGrid", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
            B(1, 4, "clusterLightIndices", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute));

    public static RhiShaderModuleDescriptor3D CreateSimulateParticles()
        => Module("gpu-driven-simulate-particles", SimulateParticles,
            B(0, 0, "frame", RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Compute),
            B(2, 0, "emitters", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(2, 1, "sourceParticles", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
            B(2, 2, "destinationParticles", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
            B(2, 3, "particleCounters", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
            B(2, 4, "particleIndirect", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute));

    public static RhiShaderModuleDescriptor3D CreateForwardVertex()
        => Module("gpu-driven-forward-vertex", ForwardVertex,
            B(0, 0, "frame", RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Vertex),
            B(0, 1, "objects", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Vertex),
            B(0, 8, "skinMatrices", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Vertex));

    public static RhiShaderModuleDescriptor3D CreateForwardFragment()
        => Module("gpu-driven-forward-fragment", ForwardFragment,
            B(0, 0, "frame", RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Fragment),
            B(0, 4, "materials", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Fragment),
            B(1, 0, "directionalLights", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Fragment),
            B(1, 1, "pointLights", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Fragment),
            B(1, 2, "spotLights", RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Fragment),
            B(1, 3, "clusterGrid", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Fragment),
            B(1, 4, "clusterLightIndices", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Fragment));

    public static RhiShaderModuleDescriptor3D CreateParticleVertex()
        => Module("gpu-driven-particle-vertex", ParticleVertex,
            B(0, 0, "frame", RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Vertex),
            B(2, 2, "particles", RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Vertex));

    public static RhiShaderModuleDescriptor3D CreateParticleFragment()
        => Module("gpu-driven-particle-fragment", ParticleFragment);

    public static RhiShaderModuleDescriptor3D CreateToneMapVertex()
        => Module("gpu-driven-tonemap-vertex", ToneMapVertex);

    public static RhiShaderModuleDescriptor3D CreateToneMapFragment()
        => Module("gpu-driven-tonemap-fragment", ToneMapFragment,
            B(3, 0, "hdrTexture", RhiBindingType3D.SampledTexture, RhiShaderStage3D.Fragment),
            B(3, 1, "linearSampler", RhiBindingType3D.Sampler, RhiShaderStage3D.Fragment));

    private static RhiShaderModuleDescriptor3D Module(string label, string identity, params RhiShaderBindingReflection3D[] bindings)
        => new(label, RhiShaderLanguage3D.Wgsl, identity, new RhiShaderReflection3D(bindings));

    private static RhiShaderBindingReflection3D B(
        int group,
        int binding,
        string name,
        RhiBindingType3D type,
        RhiShaderStage3D visibility)
        => new(group, binding, name, type, visibility);
}
