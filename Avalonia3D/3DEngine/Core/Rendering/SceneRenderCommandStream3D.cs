using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Builds the canonical backend-neutral draw stream. Opaque work remains aggressively
/// batched; transparent ordinary objects are emitted either as exact object-level commands
/// or as adaptive depth-bin/material batches when Core decides the draw-call cost is too high.
/// </summary>
public static class SceneRenderCommandStream3D
{
    public static List<SceneRenderCommand3D> Build(
        IReadOnlyList<OrdinaryRenderBatch3D> ordinaryBatches,
        IReadOnlyList<TransparentOrdinaryRenderItem3D> transparentOrdinaryItems,
        IReadOnlyList<TransparentOrdinaryBatch3D> transparentOrdinaryBatches,
        IReadOnlyList<ParticleRenderItem3D> particleItems,
        IReadOnlyList<ThreeDEngine.Core.HighScale.HighScaleInstanceLayer3D> highScaleLayers)
    {
        var capacity = (ordinaryBatches?.Count ?? 0) +
                       (transparentOrdinaryItems?.Count ?? 0) +
                       (transparentOrdinaryBatches?.Count ?? 0) +
                       (particleItems?.Count ?? 0) +
                       (highScaleLayers?.Count ?? 0);
        var commands = new List<SceneRenderCommand3D>(global::System.Math.Max(16, capacity));
        BuildInto(ordinaryBatches, transparentOrdinaryItems, transparentOrdinaryBatches, particleItems, highScaleLayers, commands);
        return commands;
    }

    public static void BuildInto(
        IReadOnlyList<OrdinaryRenderBatch3D>? ordinaryBatches,
        IReadOnlyList<TransparentOrdinaryRenderItem3D>? transparentOrdinaryItems,
        IReadOnlyList<TransparentOrdinaryBatch3D>? transparentOrdinaryBatches,
        IReadOnlyList<ParticleRenderItem3D>? particleItems,
        IReadOnlyList<ThreeDEngine.Core.HighScale.HighScaleInstanceLayer3D>? highScaleLayers,
        List<SceneRenderCommand3D> output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        output.Clear();
        var sourceOrder = 0;

        if (ordinaryBatches is not null)
        {
            for (var i = 0; i < ordinaryBatches.Count; i++)
            {
                output.Add(SceneRenderCommand3D.ForOrdinaryBatch(ordinaryBatches[i], sourceOrder++));
            }
        }

        if (highScaleLayers is not null)
        {
            for (var i = 0; i < highScaleLayers.Count; i++)
            {
                output.Add(SceneRenderCommand3D.ForHighScaleLayer(highScaleLayers[i], sourceOrder++));
            }
        }

        if (particleItems is not null)
        {
            for (var i = 0; i < particleItems.Count; i++)
            {
                output.Add(SceneRenderCommand3D.ForParticle(particleItems[i], sourceOrder++));
            }
        }

        if (transparentOrdinaryBatches is not null)
        {
            for (var i = 0; i < transparentOrdinaryBatches.Count; i++)
            {
                var batch = transparentOrdinaryBatches[i];
                if (batch.Items.Count == 0) continue;
                output.Add(SceneRenderCommand3D.ForTransparentOrdinaryBatch(batch));
            }
        }

        if (transparentOrdinaryItems is not null)
        {
            for (var i = 0; i < transparentOrdinaryItems.Count; i++)
            {
                output.Add(SceneRenderCommand3D.ForTransparentOrdinary(transparentOrdinaryItems[i]));
            }
        }

        output.Sort(SceneRenderCommand3D.CompareForDraw);
    }

    public static List<SceneRenderCommand3D> BuildShadowCasterCommands(IReadOnlyList<SceneRenderCommand3D> drawCommands)
    {
        var output = new List<SceneRenderCommand3D>(drawCommands?.Count ?? 0);
        if (drawCommands is null) return output;

        for (var i = 0; i < drawCommands.Count; i++)
        {
            var command = drawCommands[i];
            if (command.Kind == SceneRenderCommandKind3D.OrdinaryBatch ||
                command.Kind == SceneRenderCommandKind3D.TransparentOrdinaryItem ||
                command.Kind == SceneRenderCommandKind3D.TransparentOrdinaryBatch ||
                command.Kind == SceneRenderCommandKind3D.ParticleSystem ||
                command.Kind == SceneRenderCommandKind3D.HighScaleLayer)
            {
                output.Add(command);
            }
        }

        return output;
    }
}
