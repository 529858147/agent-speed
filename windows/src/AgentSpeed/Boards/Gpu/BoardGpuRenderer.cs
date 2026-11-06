using System.Numerics;
using System.Runtime.InteropServices;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Orb.Gpu;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace AgentSpeed.Boards.Gpu;

/// <summary>
/// Encodes a <see cref="BoardDrawList"/> into a render target with the orbs' shared device (DESIGN 2c, Two layers): the
/// effects through the orbs' instanced signed-distance-field pipeline and the cached layers through <c>BoardLayer.hlsl</c>,
/// batch by batch in the list's order, each batch clipped inside or outside the board's rounded square. Allocates nothing
/// on a warm frame.
/// </summary>
internal static class BoardGpuRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Uniforms
    {
        public Vector4 Viewport;
        public Vector4 Transform;
        public Vector4 Globals;
    }

    /// <summary>GPU time per board frame when enabled (the bench).</summary>
    public static GpuTimer? Timer { get; set; }

    /// <summary>Draws <paramref name="list"/> with <paramref name="layers"/> (the board's layer texture) into <paramref name="target"/>.</summary>
    public static bool Draw(GpuDevice device, BoardDrawList list, ID3D11ShaderResourceView? layers, ID3D11RenderTargetView target, int pixelWidth,
        int pixelHeight)
    {
        var r = device.Current;
        var context = r.Context;
        EnsureBuffers(r, list.Count, list.LayerCount);
        if (list.Count > 0 && !OrbGpuRenderer.Upload(context, r.Instances!, MemoryMarshal.AsBytes(list.Instances)))
        {
            return false;
        }

        if (list.LayerCount > 0 && !OrbGpuRenderer.Upload(context, r.Layers!, MemoryMarshal.AsBytes(list.Layers)))
        {
            return false;
        }

        context.OMSetRenderTargets(target);
        context.RSSetViewport(0, 0, pixelWidth, pixelHeight);
        context.RSSetState(r.Rasterizer);
        context.ClearRenderTargetView(target, new Color4(0, 0, 0, 0));

        Timer?.Begin(r);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.PSSetSampler(0, r.Sampler);
        context.PSSetShaderResource(0, r.Palette);
        if (layers is not null)
        {
            context.PSSetShaderResource(2, layers);
        }

        context.VSSetConstantBuffer(0, r.Uniforms);
        context.PSSetConstantBuffer(0, r.Uniforms);

        var canvas = (float)list.Canvas;
        var center = canvas / 2;
        var uniforms = new Uniforms
        {
            Viewport = new Vector4(canvas, canvas, canvas > 0 ? pixelWidth / canvas : 1, 0),
            Transform = new Vector4(1, center, center, (float)list.OutsideReach),
            Globals = new Vector4((float)list.Opacity, 0, (float)list.BoxHalf, (float)list.BoxRadius),
        };
        var clip = (BoardClip)(-1);
        var pass = (BoardPass)(-1);
        foreach (ref readonly var batch in list.Batches)
        {
            if (batch.Count <= 0 || (batch.Pass == BoardPass.Layers && layers is null))
            {
                continue;
            }

            if (batch.Clip != clip)
            {
                clip = batch.Clip;
                uniforms.Globals.Y = clip switch
                {
                    BoardClip.Inside => (float)list.BoxHalf,
                    BoardClip.Outside => -(float)list.BoxHalf,
                    _ => 0,
                };
                if (!OrbGpuRenderer.Upload(context, r.Uniforms, MemoryMarshal.AsBytes(new ReadOnlySpan<Uniforms>(in uniforms))))
                {
                    return false;
                }
            }

            if (batch.Pass != pass)
            {
                pass = batch.Pass;
                if (pass == BoardPass.Layers)
                {
                    context.IASetInputLayout(r.LayerLayout);
                    context.IASetVertexBuffer(0, r.Layers!, BoardLayerInstance.Stride);
                    context.VSSetShader(r.LayerVertex);
                    context.PSSetShader(r.LayerPixel);
                }
                else
                {
                    context.IASetInputLayout(r.ShapeLayout);
                    context.IASetVertexBuffer(0, r.Instances!, OrbGpuInstance.Stride);
                    context.VSSetShader(r.ShapeVertex);
                    context.PSSetShader(r.ShapePixel);
                }
            }

            context.OMSetBlendState(batch.Blend == OrbBlend.Additive ? r.Additive : r.Over);
            context.DrawInstanced(6, (uint)batch.Count, 0, (uint)batch.Start);
        }

        Timer?.End(r);
        // The layer texture is a Direct2D target too: unbind it before the next rasterization.
        context.PSSetShaderResource(2, null!);
        return true;
    }

    private static void EnsureBuffers(GpuDevice.Resources r, int instances, int layers)
    {
        if (r.Instances is null || r.InstanceCapacity < instances)
        {
            r.Instances?.Dispose();
            r.InstanceCapacity = Math.Max(1024, OrbGpuRenderer.Grow(instances));
            r.Instances = r.Device.CreateBuffer((uint)(r.InstanceCapacity * OrbGpuInstance.Stride), BindFlags.VertexBuffer, ResourceUsage.Dynamic,
                CpuAccessFlags.Write);
        }

        if (r.Layers is null || r.LayerCapacity < layers)
        {
            r.Layers?.Dispose();
            r.LayerCapacity = Math.Max(64, OrbGpuRenderer.Grow(layers));
            r.Layers = r.Device.CreateBuffer((uint)(r.LayerCapacity * BoardLayerInstance.Stride), BindFlags.VertexBuffer, ResourceUsage.Dynamic,
                CpuAccessFlags.Write);
        }
    }
}
