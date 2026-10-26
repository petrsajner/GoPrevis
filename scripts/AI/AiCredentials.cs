using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GoPrevis;

/// <summary>Local credentials only. Neither the project document nor portable builds contain keys.</summary>
public static class AiCredentials
{
    const string Service="com.petrsajner.goprevis.ai.v1";
    static readonly Dictionary<string,string> Session=new(StringComparer.Ordinal);
    internal static string[] CredentialTargets(string provider)=>new[]{provider+"."+Service,provider+".com.petrsajner.genprevis.ai.v1"};
    public static string? Read(string provider)
    {
        string[] variables=provider switch{"openrouter"=>new[]{"OPENROUTER_API_KEY"},"gemini"=>new[]{"GEMINI_API_KEY","GOOGLE_API_KEY"},_=>new[]{"OPENAI_API_KEY"}};
        foreach(string variable in variables){string? value=Environment.GetEnvironmentVariable(variable);if(!string.IsNullOrWhiteSpace(value))return value.Trim();}
        if(Session.TryGetValue(provider,out string? session))return session;
        foreach(string target in CredentialTargets(provider)){string? own=ReadWindows(target);if(!string.IsNullOrWhiteSpace(own))return own;}
        return null;
    }
    public static bool Has(string provider)=>!string.IsNullOrWhiteSpace(Read(provider));
    public static void Save(string provider,string value)
    {
        value=value.Trim();if(value.Length<12||value.Length>4096||value.Contains('\r')||value.Contains('\n'))throw new ArgumentException("Enter a valid provider API key.");
        if(!OperatingSystem.IsWindows()){Session[provider]=value;return;}
        byte[] bytes=Encoding.Unicode.GetBytes(value);IntPtr memory=Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes,0,memory,bytes.Length);
            var credential=new NativeCredential{Type=1,TargetName=provider+"."+Service,UserName=provider,CredentialBlobSize=(uint)bytes.Length,CredentialBlob=memory,Persist=2};
            if(!CredWrite(ref credential,0))throw new InvalidOperationException("The operating-system credential store could not save this connection.");
        }
        finally{CryptographicOperations.ZeroMemory(bytes);for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(memory,i,0);Marshal.FreeHGlobal(memory);}
    }
    static string? ReadWindows(string target)
    {
        if(!OperatingSystem.IsWindows()||!CredRead(target,1,0,out IntPtr pointer))return null;
        try
        {
            var credential=Marshal.PtrToStructure<NativeCredential>(pointer);
            if(credential.CredentialBlobSize==0||credential.CredentialBlobSize>8192)return null;
            byte[] bytes=new byte[credential.CredentialBlobSize];Marshal.Copy(credential.CredentialBlob,bytes,0,bytes.Length);
            try{return Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();}finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        finally{CredFree(pointer);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct NativeCredential
    {
        public uint Flags,Type;
        [MarshalAs(UnmanagedType.LPWStr)]public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;public IntPtr CredentialBlob;public uint Persist,AttributeCount;public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]public string UserName;
    }
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CredWrite(ref NativeCredential credential,uint flags);
    [DllImport("advapi32.dll")]static extern void CredFree(IntPtr credential);
}
