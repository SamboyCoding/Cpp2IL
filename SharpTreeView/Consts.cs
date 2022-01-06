using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Avalonia
{
	public static class SystemParameters
	{
		public const double MinimumHorizontalDragDistance = 2.0;
		public const double MinimumVerticalDragDistance = 2.0;
	}

	public static class SystemColors
	{
		public static IBrush ControlTextBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF000000));
		public static IBrush ControlDarkBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFA0A0A0));
		public static IBrush ControlLightBrush { get; } = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFE3E3E3));
		public static IBrush ControlBrush { get; } = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFF0F0F0));
		public static IBrush HighlightBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFFFFFFF));
		public static IBrush HighlightTextBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFFFFFFF));
		public static IBrush WindowTextBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF000000));
		public static IBrush WindowBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFFFFFFF));
		public static IBrush GrayTextBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF6D6D6D));
		public static IBrush InfoTextBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF000000));
		public static IBrush InfoBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFFFFFE1));
		public static IBrush InactiveCaptionBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFBFCDDB));
		public static IBrush InactiveCaptionTextBrush {get;} = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF000000));
		public static Color  ControlLightColor {get;} = Color.FromUInt32(0xFFE3E3E3);
		public static Color  ControlLightLightColor {get;} = Color.FromUInt32(0xFFFFFFFF);
		public static Color  ControlDarkColor {get;} = Color.FromUInt32(0xFFA0A0A0);
		public static Color  ControlDarkDarkColor {get;} = Color.FromUInt32(0xFF696969);
		public static Color  HighlightColor {get;} = Color.FromUInt32(0xFF3399FF);

		// /// <summary>
		// /// pull out values from system.drawing
		// /// </summary>
		// /// <returns></returns>
		// public static string[] GetColors()
		// {
		//     string ToString(object obj)
		//     {
		//         if(obj is ISolidColorBrush b)
		//         {
		//             return $"new ImmutableSolidColorBrush(Color.FromUInt32(0x{b.Color.ToUint32().ToString("X")}));;
		//         }
		//         else if(obj is Color c)
		//             return $"Color.FromUInt32(0x{c.ToUint32().ToString("X")});;
		//         else
		//             return obj.ToString();
		//     }
		//     return Array.ConvertAll(typeof(SystemColors).GetProperties(), p => string.Format("{0} \{get;\} = {1}", p.Name, ToString(p.GetValue(null))));
		// }

		public static Color ToAvaloniaColor(this System.Drawing.Color color)
		{
			return new Color(color.A, color.R, color.G, color.B);
		}

		//public static IBrush ToAvaloniaBrush(this System.Drawing.Brush brush)
		//{
		//	if (brush is System.Drawing.SolidBrush solidbrush) {
		//		return new ImmutableSolidColorBrush(solidbrush.Color.ToAvaloniaColor());
		//	}
		//	else if(brush is System.Drawing.TextureBrush textureBrush) {
		//		using (var imageStream = new MemoryStream()) {
		//			var image = textureBrush.Image;
		//			image.Save(imageStream, System.Drawing.Imaging.ImageFormat.Bmp);

		//			var avaloniaBitmap = new Bitmap(imageStream);
		//			return new ImageBrush(avaloniaBitmap);
		//		}

		//	} else {
		//		throw new NotSupportedException();
		//	}
		//}
	}
}
