using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace CameraHelper.Properties;

[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
[DebuggerNonUserCode]
[CompilerGenerated]
internal class Resources
{
	private static ResourceManager resourceMan;

	private static CultureInfo resourceCulture;

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static ResourceManager ResourceManager
	{
		get
		{
			if (resourceMan == null)
			{
				resourceMan = new ResourceManager("CameraHelper.Properties.Resources", typeof(Resources).Assembly);
			}
			return resourceMan;
		}
	}

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static CultureInfo Culture
	{
		get
		{
			return resourceCulture;
		}
		set
		{
			resourceCulture = value;
		}
	}

	internal static Bitmap Grid2 => (Bitmap)ResourceManager.GetObject("Grid2", resourceCulture);

	internal static Bitmap Maxi => (Bitmap)ResourceManager.GetObject("Maxi", resourceCulture);

	internal static Bitmap Mini => (Bitmap)ResourceManager.GetObject("Mini", resourceCulture);

	internal static Bitmap Review => (Bitmap)ResourceManager.GetObject("Review", resourceCulture);

	internal static Bitmap Search => (Bitmap)ResourceManager.GetObject("Search", resourceCulture);

	internal static Bitmap SettingM => (Bitmap)ResourceManager.GetObject("SettingM", resourceCulture);

	internal static Bitmap web => (Bitmap)ResourceManager.GetObject("web", resourceCulture);

	internal static Bitmap you1 => (Bitmap)ResourceManager.GetObject("you1", resourceCulture);

	internal static Bitmap zuo1 => (Bitmap)ResourceManager.GetObject("zuo1", resourceCulture);

	internal Resources()
	{
	}
}
