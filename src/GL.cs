using System;
using System.Runtime.InteropServices;

namespace Smart3DView;

/// <summary>Bağımlılıksız, en küçük OpenGL 3.3 bağlaması. GL 1.1 işlevleri opengl32.dll'den, gerisi wglGetProcAddress ile.
/// Revit'e ek DLL (OpenTK vb.) yüklemiyoruz → sürüm çakışması yok.</summary>
static unsafe class GL
{
    public const uint COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x0100;
    public const uint DEPTH_TEST = 0x0B71, SCISSOR_TEST = 0x0C11, BLEND = 0x0BE2, POLYGON_OFFSET_FILL = 0x8037, MULTISAMPLE = 0x809D, CULL_FACE = 0x0B44;
    public const uint LEQUAL = 0x0203, LESS = 0x0201, SRC_ALPHA = 0x0302, ONE_MINUS_SRC_ALPHA = 0x0303, ONE = 1;
    public const uint CLAMP_TO_EDGE = 0x812F, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803, TRIANGLE_STRIP = 5;
    public const uint TRIANGLES = 4, LINES = 1;
    public const uint UNSIGNED_INT = 0x1405, FLOAT = 0x1406, SHORT = 0x1402;
    public const uint ARRAY_BUFFER = 0x8892, ELEMENT_ARRAY_BUFFER = 0x8893, STATIC_DRAW = 0x88E4;
    public const uint VERTEX_SHADER = 0x8B31, FRAGMENT_SHADER = 0x8B30, COMPILE_STATUS = 0x8B81, LINK_STATUS = 0x8B82;
    public const uint FRAMEBUFFER = 0x8D40, READ_FRAMEBUFFER = 0x8CA8, DRAW_FRAMEBUFFER = 0x8CA9, RENDERBUFFER = 0x8D41;
    public const uint COLOR_ATTACHMENT0 = 0x8CE0, DEPTH_ATTACHMENT = 0x8D00, FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint RGBA8 = 0x8058, DEPTH_COMPONENT24 = 0x81A6, R32UI = 0x8236;
    public const uint RED_INTEGER = 0x8D94, DEPTH_COMPONENT = 0x1902, NEAREST = 0x2600, LINEAR = 0x2601, COLOR = 0x1800;
    public const uint TIME_ELAPSED = 0x88BF, QUERY_RESULT = 0x8866, QUERY_RESULT_AVAILABLE = 0x8867;
    public const uint TEXTURE_2D = 0x0DE1, TEXTURE0 = 0x84C0, RG8UI = 0x8238, RG_INTEGER = 0x8228, UNSIGNED_BYTE = 0x1401;
    public const uint TEXTURE_MIN_FILTER = 0x2801, TEXTURE_MAG_FILTER = 0x2800, UNPACK_ALIGNMENT = 0x0CF5;
    public const uint PROGRAM_POINT_SIZE = 0x8642, DYNAMIC_DRAW = 0x88E8, BGRA = 0x80E1, MAX_RENDERBUFFER_SIZE = 0x84E8, POINTS = 0, POINT_SPRITE = 0x8861;
    public const uint MAX_SAMPLES = 0x8D57, MAJOR_VERSION = 0x821B, MINOR_VERSION = 0x821C, PACK_ALIGNMENT = 0x0D05;
    public const uint VERSION = 0x1F02, RENDERER = 0x1F01;

    const string Lib = "opengl32.dll";
    [DllImport(Lib, EntryPoint = "glClearColor")] public static extern void ClearColor(float r, float g, float b, float a);
    [DllImport(Lib, EntryPoint = "glClear")] public static extern void Clear(uint mask);
    [DllImport(Lib, EntryPoint = "glViewport")] public static extern void Viewport(int x, int y, int w, int h);
    [DllImport(Lib, EntryPoint = "glEnable")] public static extern void Enable(uint cap);
    [DllImport(Lib, EntryPoint = "glDisable")] public static extern void Disable(uint cap);
    [DllImport(Lib, EntryPoint = "glDepthFunc")] public static extern void DepthFunc(uint f);
    [DllImport(Lib, EntryPoint = "glDepthMask")] public static extern void DepthMask(byte flag);
    [DllImport(Lib, EntryPoint = "glBlendFunc")] public static extern void BlendFunc(uint s, uint d);
    [DllImport(Lib, EntryPoint = "glPolygonOffset")] public static extern void PolygonOffset(float factor, float units);
    [DllImport(Lib, EntryPoint = "glLineWidth")] public static extern void LineWidth(float w);
    [DllImport(Lib, EntryPoint = "glDrawElements")] public static extern void DrawElements(uint mode, int count, uint type, IntPtr offset);
    [DllImport(Lib, EntryPoint = "glDrawArrays")] public static extern void DrawArrays(uint mode, int first, int count);
    [DllImport(Lib, EntryPoint = "glReadPixels")] public static extern void ReadPixels(int x, int y, int w, int h, uint format, uint type, void* data);
    [DllImport(Lib, EntryPoint = "glGetIntegerv")] public static extern void GetIntegerv(uint p, int* v);
    [DllImport(Lib, EntryPoint = "glGetString")] public static extern IntPtr GetStringPtr(uint name);
    [DllImport(Lib, EntryPoint = "glPixelStorei")] public static extern void PixelStorei(uint p, int v);
    [DllImport(Lib, EntryPoint = "glGetError")] public static extern uint GetError();
    [DllImport(Lib, EntryPoint = "glFinish")] public static extern void Finish();
    [DllImport(Lib, EntryPoint = "glScissor")] public static extern void Scissor(int x, int y, int w, int h);
    [DllImport(Lib, EntryPoint = "glGenTextures")] public static extern void GenTextures(int n, uint* ids);
    [DllImport(Lib, EntryPoint = "glDeleteTextures")] public static extern void DeleteTextures(int n, uint* ids);
    [DllImport(Lib, EntryPoint = "glBindTexture")] public static extern void BindTexture(uint target, uint id);
    [DllImport(Lib, EntryPoint = "glTexParameteri")] public static extern void TexParameteri(uint target, uint p, int v);
    [DllImport(Lib, EntryPoint = "glTexImage2D")] public static extern void TexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, void* data);

    [DllImport(Lib)] public static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport(Lib)] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport(Lib)] public static extern bool wglDeleteContext(IntPtr hglrc);
    [DllImport(Lib, CharSet = CharSet.Ansi)] static extern IntPtr wglGetProcAddress(string name);

    public static delegate* unmanaged<int, uint*, void> GenBuffers, DeleteBuffers, GenVertexArrays, DeleteVertexArrays,
        GenFramebuffers, DeleteFramebuffers, GenRenderbuffers, DeleteRenderbuffers;
    public static delegate* unmanaged<uint, uint, void> BindBuffer, BindFramebuffer, BindRenderbuffer, AttachShader;
    public static delegate* unmanaged<uint, nint, void*, uint, void> BufferData;
    public static delegate* unmanaged<uint, void> BindVertexArray, EnableVertexAttribArray, CompileShader, LinkProgram,
        UseProgram, DeleteProgram, DeleteShader;
    public static delegate* unmanaged<uint, int, uint, byte, int, IntPtr, void> VertexAttribPointer;
    public static delegate* unmanaged<uint, int, uint, int, IntPtr, void> VertexAttribIPointer;
    public static delegate* unmanaged<uint, uint> CreateShader;
    public static delegate* unmanaged<uint> CreateProgram;
    public static delegate* unmanaged<uint, int, byte**, int*, void> ShaderSource;
    public static delegate* unmanaged<uint, uint, int*, void> GetShaderiv, GetProgramiv;
    public static delegate* unmanaged<uint, int, int*, byte*, void> GetShaderInfoLog, GetProgramInfoLog;
    public static delegate* unmanaged<uint, byte*, int> GetUniformLocation;
    public static delegate* unmanaged<int, int, byte, float*, void> UniformMatrix4fv;
    public static delegate* unmanaged<int, int, float*, void> Uniform3fv;
    public static delegate* unmanaged<int, float, float, float, void> Uniform3f;
    public static delegate* unmanaged<int, float, float, float, float, void> Uniform4f;
    public static delegate* unmanaged<int, float, void> Uniform1f;
    public static delegate* unmanaged<int, uint, void> Uniform1ui;
    public static delegate* unmanaged<uint, uint, uint, uint, void> FramebufferRenderbuffer;
    public static delegate* unmanaged<uint, uint, int, int, void> RenderbufferStorage;
    public static delegate* unmanaged<uint, int, uint, int, int, void> RenderbufferStorageMultisample;
    public static delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> BlitFramebuffer;
    public static delegate* unmanaged<uint, uint> CheckFramebufferStatus;
    public static delegate* unmanaged<uint, int, uint*, void> ClearBufferuiv;
    public static delegate* unmanaged<int, uint*, void> GenQueries;
    public static delegate* unmanaged<uint, void> ActiveTexture;
    public static delegate* unmanaged<int, int, void> Uniform1i;
    public static delegate* unmanaged<uint, uint, void> BeginQuery;
    public static delegate* unmanaged<uint, void> EndQuery;
    public static delegate* unmanaged<uint, uint, int*, void> GetQueryObjectiv;
    public static delegate* unmanaged<uint, uint, ulong*, void> GetQueryObjectui64v;

    static bool _loaded;

    /// <summary>Geçerli bağlamla çağrılmalı. Eksik işlev varsa hata metni döner.</summary>
    public static string? Load()
    {
        if (_loaded) return null;
        try
        {
            GenBuffers = (delegate* unmanaged<int, uint*, void>)P("glGenBuffers");
            DeleteBuffers = (delegate* unmanaged<int, uint*, void>)P("glDeleteBuffers");
            GenVertexArrays = (delegate* unmanaged<int, uint*, void>)P("glGenVertexArrays");
            DeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)P("glDeleteVertexArrays");
            GenFramebuffers = (delegate* unmanaged<int, uint*, void>)P("glGenFramebuffers");
            DeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)P("glDeleteFramebuffers");
            GenRenderbuffers = (delegate* unmanaged<int, uint*, void>)P("glGenRenderbuffers");
            DeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)P("glDeleteRenderbuffers");
            BindBuffer = (delegate* unmanaged<uint, uint, void>)P("glBindBuffer");
            BindFramebuffer = (delegate* unmanaged<uint, uint, void>)P("glBindFramebuffer");
            BindRenderbuffer = (delegate* unmanaged<uint, uint, void>)P("glBindRenderbuffer");
            AttachShader = (delegate* unmanaged<uint, uint, void>)P("glAttachShader");
            BufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)P("glBufferData");
            BindVertexArray = (delegate* unmanaged<uint, void>)P("glBindVertexArray");
            EnableVertexAttribArray = (delegate* unmanaged<uint, void>)P("glEnableVertexAttribArray");
            CompileShader = (delegate* unmanaged<uint, void>)P("glCompileShader");
            LinkProgram = (delegate* unmanaged<uint, void>)P("glLinkProgram");
            UseProgram = (delegate* unmanaged<uint, void>)P("glUseProgram");
            DeleteProgram = (delegate* unmanaged<uint, void>)P("glDeleteProgram");
            DeleteShader = (delegate* unmanaged<uint, void>)P("glDeleteShader");
            VertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, IntPtr, void>)P("glVertexAttribPointer");
            VertexAttribIPointer = (delegate* unmanaged<uint, int, uint, int, IntPtr, void>)P("glVertexAttribIPointer");
            CreateShader = (delegate* unmanaged<uint, uint>)P("glCreateShader");
            CreateProgram = (delegate* unmanaged<uint>)P("glCreateProgram");
            ShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)P("glShaderSource");
            GetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)P("glGetShaderiv");
            GetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)P("glGetProgramiv");
            GetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)P("glGetShaderInfoLog");
            GetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)P("glGetProgramInfoLog");
            GetUniformLocation = (delegate* unmanaged<uint, byte*, int>)P("glGetUniformLocation");
            UniformMatrix4fv = (delegate* unmanaged<int, int, byte, float*, void>)P("glUniformMatrix4fv");
            Uniform3fv = (delegate* unmanaged<int, int, float*, void>)P("glUniform3fv");
            Uniform3f = (delegate* unmanaged<int, float, float, float, void>)P("glUniform3f");
            Uniform4f = (delegate* unmanaged<int, float, float, float, float, void>)P("glUniform4f");
            Uniform1f = (delegate* unmanaged<int, float, void>)P("glUniform1f");
            Uniform1ui = (delegate* unmanaged<int, uint, void>)P("glUniform1ui");
            FramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)P("glFramebufferRenderbuffer");
            RenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)P("glRenderbufferStorage");
            RenderbufferStorageMultisample = (delegate* unmanaged<uint, int, uint, int, int, void>)P("glRenderbufferStorageMultisample");
            BlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)P("glBlitFramebuffer");
            CheckFramebufferStatus = (delegate* unmanaged<uint, uint>)P("glCheckFramebufferStatus");
            ClearBufferuiv = (delegate* unmanaged<uint, int, uint*, void>)P("glClearBufferuiv");
            GenQueries = (delegate* unmanaged<int, uint*, void>)P("glGenQueries");
            ActiveTexture = (delegate* unmanaged<uint, void>)P("glActiveTexture");
            Uniform1i = (delegate* unmanaged<int, int, void>)P("glUniform1i");
            BeginQuery = (delegate* unmanaged<uint, uint, void>)P("glBeginQuery");
            EndQuery = (delegate* unmanaged<uint, void>)P("glEndQuery");
            GetQueryObjectiv = (delegate* unmanaged<uint, uint, int*, void>)P("glGetQueryObjectiv");
            GetQueryObjectui64v = (delegate* unmanaged<uint, uint, ulong*, void>)P("glGetQueryObjectui64v");
            _loaded = true;
            return null;
        }
        catch (MissingMethodException ex) { return ex.Message; }
    }

    static IntPtr P(string name)
    {
        var p = wglGetProcAddress(name);
        long v = p.ToInt64();
        if (v is 0 or 1 or 2 or 3 or -1) throw new MissingMethodException("OpenGL: " + name);
        return p;
    }

    public static string GetString(uint name) => Marshal.PtrToStringAnsi(GetStringPtr(name)) ?? "";

    public static uint Gen(delegate* unmanaged<int, uint*, void> fn) { uint id; fn(1, &id); return id; }
    public static void Del(delegate* unmanaged<int, uint*, void> fn, ref uint id) { if (id != 0) { uint t = id; fn(1, &t); id = 0; } }

    public static int Uniform(uint prog, string name)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* b = bytes) return GetUniformLocation(prog, b);
    }

    public static uint Program(string vs, string fs)
    {
        uint v = Shader(VERTEX_SHADER, vs), f = Shader(FRAGMENT_SHADER, fs);
        uint p = CreateProgram();
        AttachShader(p, v); AttachShader(p, f);
        LinkProgram(p);
        int ok;
        GetProgramiv(p, LINK_STATUS, &ok);
        DeleteShader(v); DeleteShader(f);
        if (ok == 0) throw new InvalidOperationException("Shader link: " + Log(p, true));
        return p;
    }

    static uint Shader(uint type, string src)
    {
        uint s = CreateShader(type);
        var bytes = System.Text.Encoding.ASCII.GetBytes(src + "\0");
        fixed (byte* b = bytes)
        {
            byte* pb = b;
            ShaderSource(s, 1, &pb, null);
        }
        CompileShader(s);
        int ok;
        GetShaderiv(s, COMPILE_STATUS, &ok);
        if (ok == 0) throw new InvalidOperationException("Shader: " + Log(s, false));
        return s;
    }

    static string Log(uint obj, bool program)
    {
        var buf = new byte[4096];
        int len;
        fixed (byte* b = buf)
        {
            if (program) GetProgramInfoLog(obj, buf.Length, &len, b);
            else GetShaderInfoLog(obj, buf.Length, &len, b);
        }
        return System.Text.Encoding.ASCII.GetString(buf, 0, Math.Max(0, len));
    }
}

static class Win32
{
    public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
    public const uint CS_OWNDC = 0x20, CS_DBLCLKS = 0x8, CS_HREDRAW = 0x2, CS_VREDRAW = 0x1;
    public const int WM_SIZE = 0x0005, WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_KEYDOWN = 0x0100,
        WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203,
        WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208,
        WM_MBUTTONDBLCLK = 0x0209, WM_MOUSEWHEEL = 0x020A, WM_CAPTURECHANGED = 0x0215, WM_GETDLGCODE = 0x0087, WM_TIMER = 0x0113, WM_SETCURSOR = 0x0020;
    public const int MK_CONTROL = 0x0008;
    public const int MK_SHIFT = 0x0004, VK_SHIFT = 0x10, VK_ESCAPE = 0x1B, VK_HOME = 0x24;
    public const uint PFD_DRAW_TO_WINDOW = 0x4, PFD_SUPPORT_OPENGL = 0x20, PFD_DOUBLEBUFFER = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize, nVersion; public uint dwFlags;
        public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift,
            cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits,
            cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public int l, t, r, b; public int fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hwnd, IntPtr id);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, IntPtr id);
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr mod, string name);
    [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr hdc);

    public static int LoWord(IntPtr p) => (short)(p.ToInt64() & 0xFFFF);
    public static int HiWord(IntPtr p) => (short)((p.ToInt64() >> 16) & 0xFFFF);
}
