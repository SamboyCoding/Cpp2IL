// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using Avalonia.Data.Converters;

namespace ICSharpCode.TreeView
{
	public class BoolConverters 
	{
		public static readonly IValueConverter Inverse = new FuncValueConverter<bool,bool>(b => !b);
	}
}
