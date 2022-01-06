// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using Avalonia;
using Avalonia.Controls.Primitives;

namespace ICSharpCode.TreeView
{
	public class GeneralAdorner : VisualLayerManager
	{
		protected override Size MeasureOverride(Size constraint)
		{
			if (Child != null) {
				Child.Measure(constraint);
				return Child.DesiredSize;
			}
			return new Size();
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if (Child != null) {
				Child.Arrange(new Rect(finalSize));
				return finalSize;
			}
			return new Size();
		}
	}
}
