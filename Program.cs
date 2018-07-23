using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SharpDX.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Device11 = SharpDX.Direct3D11.Device;
using Buffer11 = SharpDX.Direct3D11.Buffer;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Multimedia;
using SharpDX.DirectSound;

namespace TestSharpDX
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			var samples = 512;

			var devices = DirectSoundCapture.GetDevices();

			//var capture = new DirectSoundCapture(devices.OrderByDescending(d => d.Description.Contains("Mic")).First().DriverGuid);
			var capture = new DirectSoundCapture(devices.OrderByDescending(d => d.Description.Contains("Mix")).First().DriverGuid);

			var audioFormat = new WaveFormat();

			var audioBuffer = new CaptureBuffer(capture, new CaptureBufferDescription
			{
				BufferBytes = audioFormat.ConvertLatencyToByteSize(24),
				Format = audioFormat
			});

			audioBuffer.Start(true);

			using (var form = new Form())
			using (var factory = new Factory4())
			{
				form.Text = "AudioDX";
				form.ClientSize = new System.Drawing.Size(1024, 768);
				form.StartPosition = FormStartPosition.CenterScreen;

				Device11 device;
				SwapChain swapChain;

				Device11.CreateWithSwapChain(
					DriverType.Hardware,
					DeviceCreationFlags.None,
					new SwapChainDescription
					{
						IsWindowed = true,
						BufferCount = 1,
						OutputHandle = form.Handle,
						SampleDescription = new SampleDescription(1, 0),
						ModeDescription = new ModeDescription(form.ClientSize.Width, form.ClientSize.Height, new Rational(60, 1), Format.B8G8R8A8_UNorm),
						Usage = Usage.RenderTargetOutput,
						SwapEffect = SwapEffect.Discard
					},
					out device,
					out swapChain);

				var context = device.ImmediateContext;

				var backBuffer = swapChain.GetBackBuffer<Texture2D>(0);

				var backBufferView = new RenderTargetView(device, backBuffer);

				backBuffer.Dispose();

				var depthBuffer = new Texture2D(device, new Texture2DDescription
				{
					Format = Format.D16_UNorm,
					ArraySize = 1,
					MipLevels = 1,
					Width = form.ClientSize.Width,
					Height = form.ClientSize.Height,
					SampleDescription = new SampleDescription(1, 0),
					BindFlags = BindFlags.DepthStencil
				});

				var depthBufferView = new DepthStencilView(device, depthBuffer);

				depthBuffer.Dispose();

				Shapes.Sphere.Load(device);
				Shapes.Cube.Load(device);
				Shaders.Normal.Load(device);
				Shaders.Position.Load(device);

				var worldViewProjectionBuffer = new Buffer11(device, Utilities.SizeOf<Matrix>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
				var diffuseBuffer = new Buffer11(device, Utilities.SizeOf<Vector4>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);

				var rasterizerStateDescription = RasterizerStateDescription.Default();
				//rasterizerStateDescription.FillMode = FillMode.Wireframe;
				rasterizerStateDescription.IsFrontCounterClockwise = true;
				//rasterizerStateDescription.CullMode = CullMode.None;
				var rasterizerState = new RasterizerState(device, rasterizerStateDescription);

				var blendStateDescription = BlendStateDescription.Default();
				var blendState = new BlendState(device, blendStateDescription);

				var depthStateDescription = DepthStencilStateDescription.Default();

				depthStateDescription.DepthComparison = Comparison.LessEqual;
				depthStateDescription.IsDepthEnabled = true;
				depthStateDescription.IsStencilEnabled = false;

				var depthStencilState = new DepthStencilState(device, depthStateDescription);

				var samplerStateDescription = SamplerStateDescription.Default();

				samplerStateDescription.Filter = Filter.MinMagMipLinear;
				samplerStateDescription.AddressU = TextureAddressMode.Wrap;
				samplerStateDescription.AddressV = TextureAddressMode.Wrap;

				var samplerState = new SamplerState(device, samplerStateDescription);

				var startTime = DateTime.Now;
				var frame = 0;
				var size = form.ClientSize;

				var audioData = new byte[audioFormat.ConvertLatencyToByteSize(24)];
				var audioIndex = 0;

				var leftWaveForm = new float[samples * 8];
				var rightWaveForm = new float[samples * 8];

				//for (var sample = 0; sample < samples; sample++)
				//{
				//	leftWaveForm[(sample * 8) + 0] = -1.0f + ((float)sample / (samples - 1) * 2.0f);
				//	rightWaveForm[(sample * 8) + 0] = -1.0f + ((float)sample / (samples - 1) * 2.0f);
				//}

				var waveFormBufferDescription = new BufferDescription
				{
					BindFlags = BindFlags.VertexBuffer,
					SizeInBytes = leftWaveForm.Length * sizeof(float),
					CpuAccessFlags = CpuAccessFlags.Write,
					Usage = ResourceUsage.Dynamic
				};

				var leftWaveFormVertexBuffer = Buffer11.Create(device, leftWaveForm, waveFormBufferDescription);
				var rightWaveFormVertexBuffer = Buffer11.Create(device, rightWaveForm, waveFormBufferDescription);

				var leftWaveFormVertexBufferBinding = new VertexBufferBinding(leftWaveFormVertexBuffer, 8 * sizeof(float), 0);
				var rightWaveFormVertexBufferBinding = new VertexBufferBinding(rightWaveFormVertexBuffer, 8 * sizeof(float), 0);

				var leftFrequencies = new float[samples];
				var rightFrequencies = new float[samples];
				
				RenderLoop.Run(form, () =>
				{
					if (audioBuffer.CurrentCapturePosition != audioBuffer.CurrentRealPosition)
					{
						audioBuffer.Read(audioData, 0, audioData.Length, 0, LockFlags.None);

						for (var sample = 0; sample < samples; sample++)
						{
							leftWaveForm[(sample * 8) + 1] = -BitConverter.ToInt16(audioData, sample * 4) / (float)short.MinValue;
							rightWaveForm[(sample * 8) + 1] = -BitConverter.ToInt16(audioData, (sample * 4) + 2) / (float)short.MinValue;
						}

						DataStream stream;

						context.MapSubresource(leftWaveFormVertexBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out stream);

						stream.WriteRange(leftWaveForm);

						context.UnmapSubresource(leftWaveFormVertexBuffer, 0);

						stream.Dispose();

						context.MapSubresource(rightWaveFormVertexBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out stream);

						stream.WriteRange(rightWaveForm);

						context.UnmapSubresource(rightWaveFormVertexBuffer, 0);

						stream.Dispose();

						for (var sample = 0; sample < samples; sample++)
						{
							leftFrequencies[sample] = 0.0f;
							rightFrequencies[sample] = 0.0f;

							for (var sample2 = 0; sample2 < samples; sample2++)
							{
								var theta = -2.0f * MathUtil.Pi * (float)sample2 * (float)sample / (samples << 1);
								var value = (float)Math.Cos(theta);

								leftFrequencies[sample] += value * leftWaveForm[(sample2 * 8) + 1];
								rightFrequencies[sample] += value * rightWaveForm[(sample2 * 8) + 1];
							}
						}

						for (var sample = 0; sample < samples; sample++)
						{
							//leftWaveForm[(sample * 8) + 1] = Math.Abs(leftFrequencies[sample]);
							//rightWaveForm[(sample * 8) + 1] = Math.Abs(rightFrequencies[sample]);

							var angle = ((float)sample / (float)samples) * MathUtil.TwoPi;
							var sin = (float)Math.Sin(angle);
							var cos = (float)Math.Cos(angle);

							leftWaveForm[(sample * 8) + 0] = (Math.Abs(leftFrequencies[sample]) + 10.0f) * sin * -0.01f;
							leftWaveForm[(sample * 8) + 1] = (Math.Abs(leftFrequencies[sample]) + 10.0f) * cos * -0.01f;
							rightWaveForm[(sample * 8) + 0] = (Math.Abs(rightFrequencies[sample]) + 10.0f) * sin * 0.01f;
							rightWaveForm[(sample * 8) + 1] = (Math.Abs(rightFrequencies[sample]) + 10.0f) * cos * -0.01f;
						}

						context.MapSubresource(leftWaveFormVertexBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out stream);

						stream.WriteRange(leftWaveForm);

						context.UnmapSubresource(leftWaveFormVertexBuffer, 0);

						stream.Dispose();

						context.MapSubresource(rightWaveFormVertexBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out stream);

						stream.WriteRange(rightWaveForm);

						context.UnmapSubresource(rightWaveFormVertexBuffer, 0);

						stream.Dispose();
					}

					if (form.ClientSize != size)
					{
						Utilities.Dispose(ref backBufferView);
						Utilities.Dispose(ref depthBufferView);

						if (form.ClientSize.Width != 0 && form.ClientSize.Height != 0)
						{
							swapChain.ResizeBuffers(1, form.ClientSize.Width, form.ClientSize.Height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);

							backBuffer = swapChain.GetBackBuffer<Texture2D>(0);

							backBufferView = new RenderTargetView(device, backBuffer);
							backBuffer.Dispose();

							depthBuffer = new Texture2D(device, new Texture2DDescription
							{
								Format = Format.D16_UNorm,
								ArraySize = 1,
								MipLevels = 1,
								Width = form.ClientSize.Width,
								Height = form.ClientSize.Height,
								SampleDescription = new SampleDescription(1, 0),
								BindFlags = BindFlags.DepthStencil
							});

							depthBufferView = new DepthStencilView(device, depthBuffer);
							depthBuffer.Dispose();
						}

						size = form.ClientSize;
					}

					var ratio = (float)form.ClientSize.Width / (float)form.ClientSize.Height;

					var projection = Matrix.PerspectiveFovRH(3.14f / 3.0f, ratio, 0.01f, 1000);
					var view = Matrix.LookAtRH(new Vector3(0, 2, -5), Vector3.Zero, Vector3.UnitY);
					var world = Matrix.Scaling(1.0f + Math.Abs(((leftWaveForm[audioIndex + 1]) * 0.01f))) * Matrix.RotationY(Environment.TickCount / 1000.0f);
					//var world = Matrix.Scaling(1.0f + ((audioData[audioIndex] + audioData[audioIndex + 1] << 8) * 0.00001f)) * Matrix.RotationY(Environment.TickCount / 1000.0f);

					audioIndex += 8;

					if (audioIndex >= leftWaveForm.Length)
						audioIndex = 0;

					var worldViewProjection = world * view * projection;
					var diffuse = Vector4.One;

					context.UpdateSubresource(ref worldViewProjection, worldViewProjectionBuffer);
					context.UpdateSubresource(ref diffuse, diffuseBuffer);

					context.VertexShader.SetConstantBuffer(0, worldViewProjectionBuffer);
					context.PixelShader.SetConstantBuffer(1, diffuseBuffer);

					context.Rasterizer.SetViewport(0, 0, form.ClientSize.Width, form.ClientSize.Height);
					context.OutputMerger.SetTargets(depthBufferView, backBufferView);

					context.ClearRenderTargetView(backBufferView, new RawColor4(0, 0, 0, 1));
					context.ClearDepthStencilView(depthBufferView, DepthStencilClearFlags.Depth, 1.0f, 0);

					Shaders.Normal.Apply(context);
					Shapes.Sphere.Begin(context);
					//Shapes.Cube.Begin(context);

					context.Rasterizer.State = rasterizerState;
					context.OutputMerger.SetBlendState(blendState);
					context.OutputMerger.SetDepthStencilState(depthStencilState);
					context.PixelShader.SetSampler(0, samplerState);
					context.PixelShader.SetShaderResource(0, null);

					Shapes.Sphere.Draw(context);
					//Shapes.Cube.Draw(context);

					// Draw Waveforms
					Shaders.Position.Apply(context);

					//worldViewProjection = Matrix.Scaling(1, 0.01f, 1) * Matrix.Translation(0, 0.1f, 0);
					worldViewProjection = Matrix.Scaling(1, 1, 1) * Matrix.Translation(-0.5f, 0, 0);

					context.UpdateSubresource(ref worldViewProjection, worldViewProjectionBuffer);

					context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineStrip;

					context.InputAssembler.SetVertexBuffers(0, leftWaveFormVertexBufferBinding);

					context.Draw(samples, 0);

					//worldViewProjection = Matrix.Scaling(1, 0.01f, 1) * Matrix.Translation(0, -0.1f, 0);
					worldViewProjection = Matrix.Scaling(1, 1, 1) * Matrix.Translation(0.5f, 0f, 0);

					context.UpdateSubresource(ref worldViewProjection, worldViewProjectionBuffer);

					context.InputAssembler.SetVertexBuffers(0, rightWaveFormVertexBufferBinding);

					context.Draw(samples, 0);

					// Draw Frequencies
					//Shapes.Billboard.Begin(context);

					//Shapes.Billboard.Draw(context);

					swapChain.Present(1, PresentFlags.None);

					frame++;
				});

				MessageBox.Show((frame / DateTime.Now.Subtract(startTime).TotalSeconds).ToString() + " FPS");
			}

			//private float
		}
	}
}