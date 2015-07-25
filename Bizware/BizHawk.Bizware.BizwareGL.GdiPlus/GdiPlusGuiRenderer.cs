//this is full of bugs probably, related to state from old rendering sessions being all messed up. its only barely good enough to work at all

using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using sd = System.Drawing;
using System.Drawing.Imaging;

using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace BizHawk.Bizware.BizwareGL.Drivers.GdiPlus
{
	public class GDIPlusGuiRenderer : IGuiRenderer
	{
		public GDIPlusGuiRenderer(IGL_GdiPlus gl)
		{
			Owner = gl;
			Gdi = gl as IGL_GdiPlus;
		}

		OpenTK.Graphics.Color4[] CornerColors = new OpenTK.Graphics.Color4[4] {
			new OpenTK.Graphics.Color4(1.0f,1.0f,1.0f,1.0f),new OpenTK.Graphics.Color4(1.0f,1.0f,1.0f,1.0f),new OpenTK.Graphics.Color4(1.0f,1.0f,1.0f,1.0f),new OpenTK.Graphics.Color4(1.0f,1.0f,1.0f,1.0f)
		};


		public void SetCornerColor(int which, OpenTK.Graphics.Color4 color)
		{
			CornerColors[which] = color;
		}


		public void SetCornerColors(OpenTK.Graphics.Color4[] colors)
		{
			Flush(); //dont really need to flush with current implementation. we might as well roll modulate color into it too.
			if (colors.Length != 4) throw new ArgumentException("array must be size 4", "colors");
			for (int i = 0; i < 4; i++)
				CornerColors[i] = colors[i];
		}

		public void Dispose()
		{
			if (CurrentImageAttributes != null)
				CurrentImageAttributes.Dispose();
		}


		public void SetPipeline(Pipeline pipeline)
		{
		
		}

		public void SetDefaultPipeline()
		{
	
		}

		public void SetModulateColorWhite()
		{
			SetModulateColor(sd.Color.White);
		}

		ImageAttributes CurrentImageAttributes;
		public void SetModulateColor(sd.Color color)
		{
			//white is really no color at all
			if (color.ToArgb() == sd.Color.White.ToArgb())
			{
				CurrentImageAttributes.ClearColorMatrix(ColorAdjustType.Bitmap);
				return;
			}

			float r = color.R / 255.0f;
			float g = color.G / 255.0f;
			float b = color.B / 255.0f;
			float a = color.A / 255.0f;

			float[][] colorMatrixElements = { 
			 new float[] {r,  0,  0,  0,  0},
			 new float[] {0,  g,  0,  0,  0},
			 new float[] {0,  0,  b,  0,  0},
			 new float[] {0,  0,  0,  a,  0},
			 new float[] {0,  0,  0,  0,  1}};

			ColorMatrix colorMatrix = new ColorMatrix(colorMatrixElements);
			CurrentImageAttributes.SetColorMatrix(colorMatrix,ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
		}

		sd.Color CurrentModulateColor = sd.Color.White;

		IBlendState CurrentBlendState;
		public void SetBlendState(IBlendState rsBlend)
		{
			CurrentBlendState = rsBlend;
		}

		MatrixStack _Projection, _Modelview;
		public MatrixStack Projection
		{
			get { return _Projection; }
			set
			{
				_Projection = value;
				_Projection.IsDirty = true;
			}
		}
		public MatrixStack Modelview
		{
			get { return _Modelview; }
			set
			{
				_Modelview = value;
				_Modelview.IsDirty = true;
			}
		}

		public void Begin(sd.Size size) { Begin(size.Width, size.Height); }


		public void Begin(int width, int height, bool yflipped = false)
		{
			Begin();

			CurrentBlendState = Gdi.BlendNormal;

			Projection = Owner.CreateGuiProjectionMatrix(width, height);
			Modelview = Owner.CreateGuiViewMatrix(width, height);
		}


		public void Begin()
		{
			//uhhmmm I want to throw an exception if its already active, but its annoying.
			IsActive = true;
			CurrentImageAttributes = new ImageAttributes();
		}


		public void Flush()
		{
			//no batching, nothing to do here yet
		}


		public void End()
		{
			if (!IsActive)
				throw new InvalidOperationException("GuiRenderer is not active!");
			IsActive = false;
			if (CurrentImageAttributes != null)
			{
				CurrentImageAttributes.Dispose();
				CurrentImageAttributes = null;
			}
		}

		public void RectFill(float x, float y, float w, float h)
		{

		}


		public void DrawSubrect(Texture2d tex, float x, float y, float w, float h, float u0, float v0, float u1, float v1)
		{
			var tw = Gdi.TextureWrapperForTexture(tex);
			var g = Gdi.GetCurrentGraphics();
			PrepDraw(g, tw);
			float x0 = u0 * tex.Width;
			float y0 = v0 * tex.Height;
			float x1 = u1 * tex.Width;
			float y1 = v1 * tex.Height;

			//==========HACKY COPYPASTE=============

			//first we need to make a transform that will change us from the default GDI+ transformation (a top left identity transformation) to an opengl-styled one
			//(this is necessary because a 'GuiProjectionMatrix' call doesnt have any sense of the size of the destination viewport it's meant for)
			var vcb = g.VisibleClipBounds;
			float vw = vcb.Width;
			float vh = vcb.Height;
			Matrix4 fixmat = Matrix4.CreateTranslation(vw / 2, -vh / 2, 0);
			fixmat *= Matrix4.CreateScale(vw / 2, -vh / 2, 1);

			//------------------
			//( reminder: this is just an experiment: we need to turn this into a transform on the GraphicsDevice )
			//------------------
			Matrix4 mat = Projection.Top * Modelview.Top * fixmat;
			var tl = new Vector3(x, y, 0);
			var tr = new Vector3(x + w, y, 0);
			var bl = new Vector3(x, y + h, 0);
			tl = Vector3.Transform(tl, mat);
			tr = Vector3.Transform(tr, mat);
			bl = Vector3.Transform(bl, mat);

			//=======================================

			sd.PointF[] destPoints = new sd.PointF[] {
				tl.ToSDPointf(),
				tr.ToSDPointf(),
				bl.ToSDPointf(),
			};

			g.DrawImage(tw.SDBitmap, destPoints, new sd.RectangleF(x0, y0, x1 - x0, y1 - y0), sd.GraphicsUnit.Pixel, CurrentImageAttributes);
			//g.DrawImage(tw.SDBitmap, 0, 0); //test
		}


		public void Draw(Art art) { DrawInternal(art, 0, 0, art.Width, art.Height, false, false); }
		public void Draw(Art art, float x, float y) { DrawInternal(art, x, y, art.Width, art.Height, false, false); }
		public void Draw(Art art, float x, float y, float width, float height) { DrawInternal(art, x, y, width, height, false, false); }
		public void Draw(Art art, Vector2 pos) { DrawInternal(art, pos.X, pos.Y, art.Width, art.Height, false, false); }
		public void Draw(Texture2d tex) { DrawInternal(tex, 0, 0, tex.Width, tex.Height); }
		public void Draw(Texture2d tex, float x, float y) { DrawInternal(tex, x, y, tex.Width, tex.Height); }
		public void DrawFlipped(Art art, bool xflip, bool yflip) { DrawInternal(art, 0, 0, art.Width, art.Height, xflip, yflip); }

		public void Draw(Texture2d art, float x, float y, float width, float height)
		{
			DrawInternal(art, x, y, width, height);
		}

		void PrepDraw(sd.Graphics g, TextureWrapper tw)
		{
			//TODO - we can support bicubic for the final presentation..
			if ((int)tw.MagFilter != (int)tw.MinFilter)
				throw new InvalidOperationException("tw.MagFilter != tw.MinFilter");
			if (tw.MagFilter == TextureMagFilter.Linear)
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
			if (tw.MagFilter == TextureMagFilter.Nearest)
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;


			//---------

			if (CurrentBlendState == Gdi.BlendNormal)
			{
				g.CompositingMode = sd.Drawing2D.CompositingMode.SourceOver;
				g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.Default; //?

				//CurrentImageAttributes.ClearColorMatrix(ColorAdjustType.Bitmap);
			}
			else
			//if(CurrentBlendState == Gdi.BlendNoneCopy)
			//if(CurrentBlendState == Gdi.BlendNoneOpaque)
			{
				g.CompositingMode = sd.Drawing2D.CompositingMode.SourceCopy;
				g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;

				//WARNING : DO NOT USE COLOR MATRIX TO WIPE THE ALPHA
				//ITS SOOOOOOOOOOOOOOOOOOOOOOOOOOOO SLOW
				//instead, we added kind of hacky support for 24bpp images
			}

		}

		unsafe void DrawInternal(Texture2d tex, float x, float y, float w, float h)
		{
			var tw = Gdi.TextureWrapperForTexture(tex);
			var g = Gdi.GetCurrentGraphics();
			PrepDraw(g, tw);

			//first we need to make a transform that will change us from the default GDI+ transformation (a top left identity transformation) to an opengl-styled one
			//(this is necessary because a 'GuiProjectionMatrix' call doesnt have any sense of the size of the destination viewport it's meant for)
			var vcb = g.VisibleClipBounds;
			float vw = vcb.Width;
			float vh = vcb.Height;
			Matrix4 fixmat = Matrix4.CreateTranslation(vw / 2, -vh / 2, 0);
			fixmat *= Matrix4.CreateScale(vw / 2, -vh / 2, 1);

			//------------------
			//( reminder: this is just an experiment: we need to turn this into a transform on the GraphicsDevice )
			//------------------
			Matrix4 mat = Projection.Top * Modelview.Top * fixmat;
			var tl = new Vector3(x, y, 0);
			var tr = new Vector3(x + w, y, 0);
			var bl = new Vector3(x, y + h, 0);
			tl = Vector3.Transform(tl, mat);
			tr = Vector3.Transform(tr, mat);
			bl = Vector3.Transform(bl, mat);

			//a little bit of a fastpath.. I think it's safe
			//SO WHY DIDNT IT WORK?
			//anyway, it would interfere with the transforming
			//if (w == tex.Width && h == tex.Height && x == (int)x && y == (int)y)
			//  g.DrawImageUnscaled(tw.SDBitmap, (int)x, (int)y);
			sd.PointF[] destPoints = new sd.PointF[] {
				tl.ToSDPointf(),
				tr.ToSDPointf(),
				bl.ToSDPointf(),
			};

			g.DrawImage(tw.SDBitmap, destPoints, new sd.RectangleF(0, 0, tex.Width, tex.Height), sd.GraphicsUnit.Pixel, CurrentImageAttributes);
		}

		unsafe void DrawInternal(Art art, float x, float y, float w, float h, bool fx, bool fy)
		{
		
		}

		
		public bool IsActive { get; private set; }
		public IGL Owner { get; private set; }
		public IGL_GdiPlus Gdi;

	}
}