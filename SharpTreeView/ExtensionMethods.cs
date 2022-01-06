// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections;
using System.Linq;
using Avalonia.VisualTree;

namespace ICSharpCode.TreeView
{
	static class ExtensionMethods
	{
		public static T FindAncestor<T>(this IVisual d) where T : class
		{
			return d.GetVisualAncestors().OfType<T>().FirstOrDefault();
		}

		public static void AddOnce(this IList list, object item)
		{
			if (!list.Contains(item)) {
				list.Add(item);
			}
		}

		public static bool Any(this IEnumerable list)
		{
			var enumerator = list.GetEnumerator();
			var result = enumerator.MoveNext();
			(enumerator as IDisposable)?.Dispose();

			return result;
		}
	}
}
