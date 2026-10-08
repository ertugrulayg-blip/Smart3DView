using System;

namespace Smart3DView;

/// <summary>"Kağıt" tonunun kalem çizimi (kullanıcı isteği 2026-10-08): (1) her eleman ve kesit yüzeyinin dış hattı
/// ekranda bulunur — eleman no'su (+ kesit bayrağı) değişen her piksel sınırına mürekkep → kenarı olmayan borular da,
/// kutunun kestiği kanalın dikdörtgeni de çizilir; (2) model kenar çizgileri kalın, uçları taşan, hafif titrek ve
/// basıncı değişen kalem darbesi olarak çizilir. GPU bunları desteklemezse düz çizgilere dönülür.</summary>
sealed unsafe partial class GlView
{
    uint _pId, _pInk, _pEdgeSk;
    int idMvp, idBoxMin, idBoxMax, idState, idCam, idPass, inkId, inkView, inkR, inkColor;
    int kMvp, kColor, kSel, kSelColor, kBoxMin, kBoxMax, kState, kClash, kFade, kView, kWidth, kOver;
    uint _idFbo, _idTex, _idDepth;
    int _idW, _idH;
    bool _sketchOk;

    // Eleman no'su + kesit bayrağı (üst bit): ana çizimle aynı iki geçiş (dış yüzler, sonra kesit iç yüzleri).
    const string IdVs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNrm;
layout(location=2) in int aTone;
layout(location=3) in uint aId;
uniform mat4 uMvp;
out vec3 vPos; out vec3 vNrm; flat out int vTone; flat out uint vId;
void main(){ vPos=aPos; vNrm=aNrm; vTone=aTone; vId=aId; gl_Position=uMvp*vec4(aPos,1.0); }";

    const string IdFs = @"#version 330 core
in vec3 vPos; in vec3 vNrm; flat in int vTone; flat in uint vId;
uniform vec3 uBoxMin; uniform vec3 uBoxMax; uniform usampler2D uState; uniform vec3 uCam; uniform int uPass;
out uint o;
void main(){
  if (any(lessThan(vPos, uBoxMin)) || any(greaterThan(vPos, uBoxMax))) discard;
  if ((texelFetch(uState, ivec2(int(vId % 4096u), int(vId / 4096u)), 0).r & 128u) != 0u) discard;
  vec3 g = cross(dFdx(vPos), dFdy(vPos));
  if (dot(g, vPos - uCam) > 0.0) g = -g;
  bool inside = vTone != 4 && dot(g, vNrm) < 0.0;
  if (inside != (uPass == 1)) discard;
  o = vId | (inside ? 0x80000000u : 0u);
}";

    const string InkVs = @"#version 330 core
void main(){
  vec2 p = vec2(gl_VertexID == 1 ? 3.0 : -1.0, gl_VertexID == 2 ? 3.0 : -1.0);
  gl_Position = vec4(p, 0.0, 1.0);
}";

    // Sınır: uR yarıçap içinde farklı no'lu piksel varsa mürekkep. Okuma noktası düşük frekanslı gürültüyle kaydırılır
    // (titrek el), koyuluk da gürültüyle değişir (kalem basıncı).
    const string InkFs = @"#version 330 core
uniform usampler2D uId; uniform vec3 uView; uniform float uR; uniform vec3 uInk;
out vec4 o;
float h(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float n(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
  return mix(mix(h(i), h(i + vec2(1, 0)), f.x), mix(h(i + vec2(0, 1)), h(i + vec2(1, 1)), f.x), f.y); }
uint at(ivec2 c){ return texelFetch(uId, clamp(c, ivec2(0), ivec2(uView.xy) - 1), 0).r; }
void main(){
  vec2 p = gl_FragCoord.xy;
  vec2 w = vec2(n(p / 31.0), n(p / 31.0 + 17.3)) - 0.5;
  ivec2 c = ivec2(p + w * 2.2 * max(uR, 1.0));
  uint id0 = at(c);
  int r = int(ceil(uR));
  float best = 1e9;
  for (int y = -r; y <= r; y++)
    for (int x = -r; x <= r; x++) {
      float d = length(vec2(x, y));
      if (d > uR || d >= best) continue;
      if (at(c + ivec2(x, y)) != id0) best = d;
    }
  if (best > uR) discard;
  float a = (0.72 + 0.28 * n(p / 7.0 + 3.1)) * clamp(uR + 0.6 - best, 0.0, 1.0);
  o = vec4(uInk, a);
}";

    const string SkEdgeVs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in uint aId;
uniform mat4 uMvp;
out vec3 gPos; flat out uint gId;
void main(){ gPos=aPos; gId=aId; gl_Position=uMvp*vec4(aPos,1.0); }";

    // Her çizgi parçası ekranda kalın bir şerit: uçları taşar (kısa parçalar — eğri dilimleri — az taşar), iki ucu
    // farklı miktarda yana kayar (hafif eğik, el çizimi), kalınlık ve koyuluk parça parça değişir.
    const string SkEdgeGs = @"#version 330 core
layout(lines) in; layout(triangle_strip, max_vertices = 4) out;
in vec3 gPos[]; flat in uint gId[];
out vec3 vPos; flat out uint vId; out float vA;
uniform vec3 uView; uniform float uWidth; uniform float uOver;
float h(float x){ return fract(sin(x) * 43758.5453); }
void emit(vec4 c, vec2 px, vec3 wp){ gl_Position = vec4(px / (0.5 * uView.xy) * c.w, c.z, c.w); vPos = wp; vId = gId[0]; EmitVertex(); }
void main(){
  vec4 a = gl_in[0].gl_Position, b = gl_in[1].gl_Position;
  vec3 pa = gPos[0], pb = gPos[1];
  const float e = 1e-4;
  if (a.w < e && b.w < e) return;
  if (a.w < e) { float t = (e - a.w) / (b.w - a.w); a = mix(a, b, t); pa = mix(pa, pb, t); }
  else if (b.w < e) { float t = (e - b.w) / (a.w - b.w); b = mix(b, a, t); pb = mix(pb, pa, t); }
  vec2 sa = a.xy / a.w * 0.5 * uView.xy, sb = b.xy / b.w * 0.5 * uView.xy;
  vec2 d = sb - sa; float L = length(d);
  if (L < 0.01) return;
  vec2 t = d / L, nn = vec2(-t.y, t.x);
  float s = h(dot(pa + pb, vec3(12.9898, 78.233, 37.719)));
  float k = min(1.0, L / 45.0);
  float oa = uOver * k * (0.4 + 0.9 * h(s * 7.1)), ob = uOver * k * (0.4 + 0.9 * h(s * 3.7));
  float ja = (h(s * 11.3) - 0.5) * 1.1 * k, jb = (h(s * 5.9) - 0.5) * 1.1 * k;
  float hw = 0.5 * uWidth * (0.8 + 0.4 * h(s * 91.7));
  vA = 0.7 + 0.3 * h(s * 13.1);
  vec2 ea = sa - t * oa + nn * ja, eb = sb + t * ob + nn * jb;
  vec3 wa = pa - (pb - pa) * (oa / L), wb = pb + (pb - pa) * (ob / L);
  emit(a, ea + nn * hw, wa); emit(a, ea - nn * hw, wa);
  emit(b, eb + nn * hw, wb); emit(b, eb - nn * hw, wb);
  EndPrimitive();
}";

    const string SkEdgeFs = @"#version 330 core
in vec3 vPos; flat in uint vId; in float vA;
uniform vec3 uColor; uniform uint uSel; uniform vec3 uSelColor; uniform vec3 uBoxMin; uniform vec3 uBoxMax;
uniform usampler2D uState; uniform int uClash; uniform vec3 uFade;
out vec4 o;
void main(){
  if (any(lessThan(vPos, uBoxMin)) || any(greaterThan(vPos, uBoxMax))) discard;
  vec3 c = uColor;
  uvec2 st = texelFetch(uState, ivec2(int(vId % 4096u), int(vId / 4096u)), 0).rg;
  if ((st.r & 128u) != 0u) discard;
  uint ck = st.g & 3u;
  if (uClash != 0) c = ck == 1u ? vec3(0.45, 0.0, 0.0) : ck == 2u ? vec3(0.0, 0.12, 0.5) : mix(uColor, uFade, 0.75);
  if (uSel != 0u && vId == uSel) c = uSelColor;
  o = vec4(c, vA);
}";

    void InitSketchGl()
    {
        try
        {
            _pId = GL.Program(IdVs, IdFs);
            idMvp = GL.Uniform(_pId, "uMvp"); idBoxMin = GL.Uniform(_pId, "uBoxMin"); idBoxMax = GL.Uniform(_pId, "uBoxMax");
            idState = GL.Uniform(_pId, "uState"); idCam = GL.Uniform(_pId, "uCam"); idPass = GL.Uniform(_pId, "uPass");
            _pInk = GL.Program(InkVs, InkFs);
            inkId = GL.Uniform(_pInk, "uId"); inkView = GL.Uniform(_pInk, "uView"); inkR = GL.Uniform(_pInk, "uR"); inkColor = GL.Uniform(_pInk, "uInk");
            _pEdgeSk = GL.Program(SkEdgeVs, SkEdgeGs, SkEdgeFs);
            kMvp = GL.Uniform(_pEdgeSk, "uMvp"); kColor = GL.Uniform(_pEdgeSk, "uColor"); kSel = GL.Uniform(_pEdgeSk, "uSel");
            kSelColor = GL.Uniform(_pEdgeSk, "uSelColor"); kBoxMin = GL.Uniform(_pEdgeSk, "uBoxMin"); kBoxMax = GL.Uniform(_pEdgeSk, "uBoxMax");
            kState = GL.Uniform(_pEdgeSk, "uState"); kClash = GL.Uniform(_pEdgeSk, "uClash"); kFade = GL.Uniform(_pEdgeSk, "uFade");
            kView = GL.Uniform(_pEdgeSk, "uView"); kWidth = GL.Uniform(_pEdgeSk, "uWidth"); kOver = GL.Uniform(_pEdgeSk, "uOver");
            _sketchOk = true;
        }
        catch { _sketchOk = false; }   // eski sürücü: Kağıt tonu düz çizgilerle çalışır
    }

    void FreeSketchGl()
    {
        foreach (var p in new[] { _pId, _pInk, _pEdgeSk }) if (p != 0) GL.DeleteProgram(p);
        _pId = _pInk = _pEdgeSk = 0;
        FreeIdFbo();
    }

    void FreeIdFbo()
    {
        GL.Del(GL.DeleteFramebuffers, ref _idFbo);
        GL.Del(GL.DeleteRenderbuffers, ref _idDepth);
        if (_idTex != 0) { uint t = _idTex; GL.DeleteTextures(1, &t); _idTex = 0; }
        _idW = _idH = 0;
    }

    bool EnsureIdFbo(int w, int h)
    {
        if (_idFbo != 0 && _idW == w && _idH == h) return true;
        FreeIdFbo();
        _idW = w; _idH = h;
        uint t;
        GL.GenTextures(1, &t);
        _idTex = t;
        GL.BindTexture(GL.TEXTURE_2D, _idTex);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.NEAREST);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.NEAREST);
        GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.R32UI, w, h, 0, GL.RED_INTEGER, GL.UNSIGNED_INT, null);
        GL.BindTexture(GL.TEXTURE_2D, 0);
        _idFbo = GL.Gen(GL.GenFramebuffers);
        GL.BindFramebuffer(GL.FRAMEBUFFER, _idFbo);
        GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.TEXTURE_2D, _idTex, 0);
        _idDepth = Rb(GL.DEPTH_COMPONENT24, 0, GL.DEPTH_ATTACHMENT, w, h);
        bool ok = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
        if (!ok) FreeIdFbo();
        return ok;
    }

    bool Sketch => _pal.Sketch && _sketchOk;

    /// <summary>Kalem kenar çizgileri (geometri shader'ı ile şerit). DrawScene'deki düz çizgilerin yerine.</summary>
    void DrawSketchEdges(float[] mvp, (float r, float g, float b) fade, float lineWidth, int vw, int vh)
    {
        GL.UseProgram(_pEdgeSk);
        fixed (float* m = mvp) GL.UniformMatrix4fv(kMvp, 1, 1, m);
        U3(kColor, _pal.Edge);
        GL.Uniform1ui(kSel, Selected);
        GL.Uniform3f(kSelColor, 0.08f, 0.36f, 0.85f);
        UBox(kBoxMin, kBoxMax);
        GL.Uniform1i(kState, 0);
        GL.Uniform1i(kClash, _clashMode ? 1 : 0);
        GL.Uniform3f(kFade, fade.r, fade.g, fade.b);
        GL.Uniform3f(kView, vw, vh, 0);
        GL.Uniform1f(kWidth, lineWidth);
        GL.Uniform1f(kOver, 3.5f * lineWidth);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.BindVertexArray(_evao);
        GL.DrawArrays(GL.LINES, 0, _edgeCount);
        GL.Disable(GL.BLEND);
    }

    /// <summary>Dış hat ve kesit çizgileri: eleman no'su görüntüsü çizilir, sınırlarına mürekkep basılır.</summary>
    void DrawInk(uint fbo, int vw, int vh, float[] mvp, float lineWidth)
    {
        if (!EnsureIdFbo(vw, vh)) { GL.BindFramebuffer(GL.FRAMEBUFFER, fbo); GL.Viewport(0, 0, vw, vh); return; }
        GL.BindFramebuffer(GL.FRAMEBUFFER, _idFbo);
        GL.Viewport(0, 0, vw, vh);
        uint* zero = stackalloc uint[4];
        GL.ClearBufferuiv(GL.COLOR, 0, zero);
        GL.DepthMask(1);
        GL.Clear(GL.DEPTH_BUFFER_BIT);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthFunc(GL.LEQUAL);
        GL.Enable(GL.POLYGON_OFFSET_FILL);
        GL.PolygonOffset(1f, 1f);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _stateTex);
        GL.UseProgram(_pId);
        fixed (float* m = mvp) GL.UniformMatrix4fv(idMvp, 1, 1, m);
        UBox(idBoxMin, idBoxMax);
        GL.Uniform1i(idState, 0);
        GL.Uniform3f(idCam, (float)_pos.X, (float)_pos.Y, (float)_pos.Z);
        GL.Uniform1i(idPass, 0);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _iboO);
        GL.DrawElements(GL.TRIANGLES, _opaqueCount, GL.UNSIGNED_INT, IntPtr.Zero);
        if (BoxCutsGeometry())
        {
            GL.Uniform1i(idPass, 1);
            GL.PolygonOffset(-1f, -2f);
            GL.DrawElements(GL.TRIANGLES, _opaqueCount, GL.UNSIGNED_INT, IntPtr.Zero);
        }
        GL.Disable(GL.POLYGON_OFFSET_FILL);
        GL.BindVertexArray(0);

        GL.BindFramebuffer(GL.FRAMEBUFFER, fbo);
        GL.Viewport(0, 0, vw, vh);
        GL.Disable(GL.DEPTH_TEST);
        GL.DepthMask(0);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.UseProgram(_pInk);
        GL.BindTexture(GL.TEXTURE_2D, _idTex);
        GL.Uniform1i(inkId, 0);
        GL.Uniform3f(inkView, vw, vh, 0);
        GL.Uniform1f(inkR, Math.Max(0.8f, 0.75f * lineWidth));
        U3(inkColor, _pal.Edge);
        GL.BindVertexArray(_bgVao);
        GL.DrawArrays(GL.TRIANGLES, 0, 3);
        GL.BindVertexArray(0);
        GL.BindTexture(GL.TEXTURE_2D, 0);
        GL.Disable(GL.BLEND);
        GL.DepthMask(1);
        GL.Enable(GL.DEPTH_TEST);
    }
}
