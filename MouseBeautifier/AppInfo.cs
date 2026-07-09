using System.Reflection;

namespace MouseBeautifier
{
    /// <summary>
    /// 集中管理应用的版本号、作者、仓库等元数据。
    /// 值来自 csproj 中的 &lt;Version&gt; / &lt;Authors&gt; / &lt;Description&gt; 等属性，
    /// 通过 AssemblyName 的 AssemblyProductAttribute / AssemblyCopyrightAttribute 等读取。
    /// </summary>
    internal static class AppInfo
    {
        private static readonly Assembly _asm = typeof(AppInfo).Assembly;

        /// <summary>语义化版本号（如 "1.1.0"），来自 csproj 的 &lt;Version&gt;。
        /// 自动去除 SourceLink 追加的 "+commitHash" 后缀，保证显示干净。</summary>
        public static string Version
        {
            get
            {
                var v = _asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var raw = v?.InformationalVersion ?? _asm.GetName().Version?.ToString() ?? "1.0.0";
                int plus = raw.IndexOf('+');
                return plus >= 0 ? raw.Substring(0, plus) : raw;
            }
        }

        /// <summary>作者，来自 csproj 的 &lt;Authors&gt;。</summary>
        public static string Author
        {
            get
            {
                var a = _asm.GetCustomAttribute<AssemblyCompanyAttribute>();
                return a?.Company ?? "sxwzxc";
            }
        }

        /// <summary>产品名，来自 csproj 的 &lt;Product&gt;。</summary>
        public static string Product
        {
            get
            {
                var p = _asm.GetCustomAttribute<AssemblyProductAttribute>();
                return p?.Product ?? "FunnyCursor";
            }
        }

        /// <summary>版权信息，来自 csproj 的 &lt;Copyright&gt;。</summary>
        public static string Copyright
        {
            get
            {
                var c = _asm.GetCustomAttribute<AssemblyCopyrightAttribute>();
                return c?.Copyright ?? "Copyright © 2026 sxwzxc";
            }
        }

        /// <summary>描述，来自 csproj 的 &lt;Description&gt;。</summary>
        public static string Description
        {
            get
            {
                var d = _asm.GetCustomAttribute<AssemblyDescriptionAttribute>();
                return d?.Description ?? "Windows 鼠标美化工具";
            }
        }

        /// <summary>GitHub 仓库地址。</summary>
        public const string RepositoryUrl = "https://github.com/sxwzxc/FunnyCursor";
    }
}
