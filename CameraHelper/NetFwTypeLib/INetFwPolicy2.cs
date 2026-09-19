using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetFwTypeLib;

[ComImport]
[CompilerGenerated]
[Guid("98325047-C671-4174-8D81-DEFCD3F03186")]
[TypeIdentifier]
public interface INetFwPolicy2
{
	void _VtblGap1_1();

	[DispId(2)]
	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	bool get_FirewallEnabled([In] NET_FW_PROFILE_TYPE2_ profileType);

	[DispId(2)]
	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	void set_FirewallEnabled([In] NET_FW_PROFILE_TYPE2_ profileType, [In] bool enabled);
}
