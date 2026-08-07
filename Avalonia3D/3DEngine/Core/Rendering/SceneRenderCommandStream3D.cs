using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Builds the canonical backend-neutral draw stream. Opaque work remains aggressively
/// batched; transparent ordinary objects are emitted either as exact object-level commands
/// or as adaptive depth-bin/material batches when Core decides the draw-call cost is too high.
/// </summary>
internal static class SceneRenderCommandStream3D
{
    public static void BuildInto(
        IReadOnlyList<OrdinaryRenderBatch3D>? ordinaryBatches,
        IReadOnlyList<TransparentOrdinaryRenderItem3D>? transparentOrdinaryItems,
        IReadOnlyList<TransparentOrdinaryBatch3D>? transparentOrdinaryBatches,
        IReadOnlyList<ParticleRenderItem3D>? particleItems,
        IReadOnlyList<ThreeDEngine.Core.HighScale.HighScaleInstanceLayer3D>? highScaleLayers,
        List<SceneRenderCommand3D> output,
        SceneRenderPlanScratch3D scratch)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));
        output.Clear();
        var sourceOrder = 0;

        if (ordinaryBatches is not null)
        {
            for (var i = 0; i < ordinaryBatches.Count; i++)
            {
                var order = sourceOrder++;
                output.Add(scratch.RentOrdinaryCommand(ordinaryBatches[i], order));
            }
        }

        if (highScaleLayers is not null)
        {
            for (var i = 0; i < highScaleLayers.Count; i++)
            {
                var order = sourceOrder++;
                output.Add(scratch.RentHighScaleCommand(highScaleLayers[i], order));
            }
        }

        if (particleItems is not null)
        {
            for (var i = 0; i < particleItems.Count; i++)
            {
                var order = sourceOrder++;
                output.Add(scratch.RentParticleCommand(particleItems[i], order));
            }
        }

        if (transparentOrdinaryBatches is not null)
        {
            for (var i = 0; i < transparentOrdinaryBatches.Count; i++)
            {
                var batch = transparentOrdinaryBatches[i];
                if (batch.Items.Count == 0) continue;
                output.Add(scratch.RentTransparentBatchCommand(batch));
            }
        }

        if (transparentOrdinaryItems is not null)
        {
            for (var i = 0; i < transparentOrdinaryItems.Count; i++)
            {
                output.Add(scratch.RentTransparentCommand(transparentOrdinaryItems[i]));
            }
        }

        output.Sort(SceneRenderCommand3D.CompareForDraw);
    }

}
