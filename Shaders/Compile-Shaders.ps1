param([string]$Source = (Join-Path $PSScriptRoot 'LiquidGlass.hlsl'), [string]$Output = (Join-Path $PSScriptRoot 'LiquidGlass.ps'))
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class IslandShaderCompiler {
    [ComImport, Guid("8BA5FB08-5195-40E2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Blob {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] UIntPtr GetBufferSize();
    }
    [DllImport("d3dcompiler_47.dll", CallingConvention=CallingConvention.StdCall)]
    private static extern int D3DCompile(byte[] source, UIntPtr length,
        [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1, uint flags2, out Blob code, out Blob errors);
    public static void Compile(string input, string output) {
        byte[] source = File.ReadAllBytes(input);
        Blob code = null, errors = null;
        try {
            int hr = D3DCompile(source, (UIntPtr)source.Length, input, IntPtr.Zero, IntPtr.Zero,
                "main", "ps_2_0", 1u << 15, 0, out code, out errors);
            if (hr < 0) throw new Exception(errors == null ? "D3DCompile failed" : Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
            byte[] bytes = new byte[checked((int)code.GetBufferSize().ToUInt64())];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            File.WriteAllBytes(output, bytes);
        } finally {
            if (code != null) Marshal.ReleaseComObject(code);
            if (errors != null) Marshal.ReleaseComObject(errors);
        }
    }
}
'@
[IslandShaderCompiler]::Compile($Source, $Output)
Write-Host 'Compiled LiquidGlass.ps (WPF ps_2_0).'
