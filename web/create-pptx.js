// @ts-check
const PptxGenJS = require("pptxgenjs");
const pptx = new PptxGenJS();
pptx.layout = "LAYOUT_WIDE"; // 13.33" x 7.5"

/* ── Renk paleti ── */
const C = {
  bg:           "030712",
  card:         "111827",
  cardBorder:   "1f2937",
  accent:       "1d4ed8",
  accentMid:    "2563eb",
  blue:         "3b82f6",
  blueLight:    "93c5fd",
  bluePill:     "bfdbfe",
  text:         "f9fafb",
  textSub:      "e5e7eb",
  textMuted:    "9ca3af",
  textDim:      "4b5563",
  green:        "22c55e",
  greenDark:    "14532d",
  greenLight:   "86efac",
  red:          "ef4444",
  redLight:     "f87171",
  yellow:       "eab308",
  yellowLight:  "facc15",
  purple:       "a855f7",
  purpleDark:   "3b0764",
  purpleLight:  "d8b4fe",
  navy:         "1e3a5f",
};

/* ── Yardımcı: slayt oluştur ── */
function S() {
  const slide = pptx.addSlide();
  slide.background = { color: C.bg };
  return slide;
}

/* ── Yardımcı: kart ── */
function card(slide, x, y, w, h, fill = C.card, border = C.cardBorder) {
  slide.addShape(pptx.ShapeType.roundRect, {
    x, y, w, h,
    fill: { color: fill },
    line: { color: border, width: 0.5 },
    rectRadius: 0.08,
  });
}

/* ── Yardımcı: etiket (tag chip) ── */
function tag(slide, text) {
  const w = 3.8, x = (13.33 - w) / 2, y = 0.42;
  slide.addShape(pptx.ShapeType.roundRect, { x, y, w, h: 0.3, fill: { color: C.accent }, line: { color: C.accent }, rectRadius: 0.04 });
  slide.addText(text.toUpperCase(), { x, y, w, h: 0.3, fontSize: 8, color: C.bluePill, bold: true, align: "center", valign: "middle", charSpacing: 2 });
}

/* ── Yardımcı: büyük başlık ── */
function title(slide, text, y = 0.95) {
  slide.addText(text, { x: 0.5, y, w: 12.33, h: 0.75, fontSize: 30, color: C.text, bold: true, align: "center" });
}

/* ── Yardımcı: alt başlık ── */
function sub(slide, text, y = 1.8) {
  slide.addText(text, { x: 1.5, y, w: 10.33, h: 0.55, fontSize: 13, color: C.textDim, align: "center" });
}

/* ── Yardımcı: liste satırı ── */
function listRow(slide, x, y, dotColor, text, bold = false) {
  slide.addShape(pptx.ShapeType.ellipse, { x: x + 0.05, y: y + 0.1, w: 0.13, h: 0.13, fill: { color: dotColor }, line: { color: dotColor } });
  slide.addText(text, { x: x + 0.28, y, w: 11.2, h: 0.35, fontSize: 13, color: bold ? C.textSub : C.textMuted, bold });
}

/* ── Yardımcı: ayırıcı çizgi ── */
function divider(slide, y) {
  slide.addShape(pptx.ShapeType.line, { x: 0.5, y, w: 12.33, h: 0, line: { color: C.cardBorder, width: 0.5 } });
}

/* ════════════════════════════════════════
   SLAYT 1 — KAPAK
════════════════════════════════════════ */
{
  const slide = S();

  // Logo kutusu
  slide.addShape(pptx.ShapeType.roundRect, { x: 5.92, y: 1.0, w: 1.5, h: 1.5, fill: { color: C.accentMid }, line: { color: C.accentMid }, rectRadius: 0.18 });
  slide.addText("📊", { x: 5.92, y: 1.0, w: 1.5, h: 1.5, fontSize: 40, align: "center", valign: "middle" });

  // Etiket
  tag(slide, "Bitirme Projesi");

  // Ana başlık
  slide.addText("API Insight Studio", { x: 0.5, y: 2.75, w: 12.33, h: 0.95, fontSize: 44, color: C.text, bold: true, align: "center" });

  // Alt başlık
  slide.addText("Yapay Zeka Destekli API Kalite ve Güvenlik Analiz Platformu", {
    x: 1.5, y: 3.8, w: 10.33, h: 0.5, fontSize: 16, color: C.textDim, align: "center",
  });

  // Ayırıcı
  slide.addShape(pptx.ShapeType.roundRect, { x: 6.17, y: 4.55, w: 1.0, h: 0.04, fill: { color: C.cardBorder }, line: { color: C.cardBorder } });

  // İsim
  slide.addText("Erdoğan Doğan", { x: 0.5, y: 4.75, w: 12.33, h: 0.4, fontSize: 13, color: C.textDim, align: "center" });
}

/* ════════════════════════════════════════
   SLAYT 2 — PROBLEM
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Problem");
  title(slide, "Neden Bu Proje?");
  sub(slide, "API'ler yazılım projelerinin omurgasıdır — ancak çoğu zaman dokümansız ve güvensiz bırakılır.", 1.75);

  const items = [
    { color: C.red,    text: "Endpoint açıklamaları eksik → geliştiriciler ne işe yaradığını bilemez" },
    { color: C.red,    text: "Hata durum kodları tanımlı değil → beklenmedik davranışlar ortaya çıkar" },
    { color: C.red,    text: "POST/PUT/DELETE'te kimlik doğrulama eksik → ciddi güvenlik açığı" },
    { color: C.red,    text: "DELETE + {id} parametresi → yetkisiz veri silme riski (BOLA)" },
    { color: C.yellow, text: "Bu kontroller elle yapılıyor → zaman kaybı ve insan hatası" },
  ];

  items.forEach((item, i) => {
    const y = 2.5 + i * 0.82;
    card(slide, 0.5, y, 12.33, 0.65);
    listRow(slide, 0.7, y + 0.15, item.color, item.text);
  });
}

/* ════════════════════════════════════════
   SLAYT 3 — ÇÖZÜM
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Çözüm");
  title(slide, "API Insight Studio");
  sub(slide, "Swagger/OpenAPI dosyasını yükle — sistem otomatik analiz eder, puan verir, uyarıları listeler.", 1.75);

  const feats = [
    { icon: "📊", color: C.navy,       title: "Otomatik Analiz",   desc: "API'nin kalite ve güvenlik\nkurallarına uygunluğunu\n100 üzerinden puanlar" },
    { icon: "🤖", color: C.greenDark,  title: "Yapay Zeka",        desc: "Eksik açıklamalar için yerel\nAI modeli ile Türkçe otomatik\ndokümantasyon üretir" },
    { icon: "🧪", color: C.purpleDark, title: "Test Senaryoları",  desc: "Her endpoint için otomatik\ntest senaryoları oluşturur\nve raporlar" },
  ];

  feats.forEach((f, i) => {
    const x = 0.5 + i * 4.28;
    card(slide, x, 2.5, 4.05, 3.8, C.card, f.color);
    slide.addShape(pptx.ShapeType.roundRect, { x: x + 0.25, y: 2.75, w: 0.7, h: 0.7, fill: { color: f.color }, line: { color: f.color }, rectRadius: 0.08 });
    slide.addText(f.icon, { x: x + 0.25, y: 2.75, w: 0.7, h: 0.7, fontSize: 20, align: "center", valign: "middle" });
    slide.addText(f.title, { x: x + 0.2, y: 3.6, w: 3.65, h: 0.4, fontSize: 15, color: C.textSub, bold: true });
    slide.addText(f.desc, { x: x + 0.2, y: 4.1, w: 3.65, h: 1.5, fontSize: 12, color: C.textDim });
  });
}

/* ════════════════════════════════════════
   SLAYT 4 — MİMARİ
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Teknik Mimari");
  title(slide, "Sistem Mimarisi");

  const boxes = [
    { icon: "🌐", title: "Web / Masaüstü", detail: "Next.js 16\nElectron",            border: C.cardBorder },
    { icon: "⚙️", title: "REST API",       detail: "ASP.NET Core\nJWT Auth",          border: C.navy },
    { icon: "🗄️", title: "Veritabanı",    detail: "MSSQL\nEntity Framework Core",    border: C.cardBorder },
    { icon: "🤖", title: "AI Servisi",     detail: "Ollama\nqwen2.5:7b",              border: C.greenDark },
  ];

  boxes.forEach((b, i) => {
    const x = 0.5 + i * 3.2;
    card(slide, x, 2.1, 2.8, 2.6, C.card, b.border);
    slide.addText(b.icon, { x, y: 2.2, w: 2.8, h: 0.7, fontSize: 28, align: "center", valign: "middle" });
    slide.addText(b.title, { x, y: 3.0, w: 2.8, h: 0.4, fontSize: 13, color: C.textSub, bold: true, align: "center" });
    slide.addText(b.detail, { x, y: 3.45, w: 2.8, h: 0.8, fontSize: 11, color: C.textDim, align: "center" });
    if (i < 3) {
      slide.addText("→", { x: x + 2.8, y: 2.9, w: 0.4, h: 0.5, fontSize: 20, color: C.cardBorder, align: "center" });
    }
  });

  // Teknoloji etiketleri
  const techs = ["Next.js 16", "React 19", "Tailwind CSS 4", "Electron", "ASP.NET Core", "Entity Framework", "MSSQL", "Ollama · qwen2.5:7b", "JWT", "OpenAPI"];
  let tx = 0.5, ty = 5.1;
  techs.forEach((t) => {
    const w = t.length * 0.095 + 0.4;
    if (tx + w > 12.8) { tx = 0.5; ty += 0.52; }
    slide.addShape(pptx.ShapeType.roundRect, { x: tx, y: ty, w, h: 0.36, fill: { color: C.card }, line: { color: C.cardBorder, width: 0.5 }, rectRadius: 0.06 });
    slide.addText(t, { x: tx, y: ty, w, h: 0.36, fontSize: 10, color: C.textMuted, align: "center", valign: "middle" });
    tx += w + 0.12;
  });
}

/* ════════════════════════════════════════
   SLAYT 5 — ÇOKLU PLATFORM
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Çoklu Platform");
  title(slide, "İki Platform Desteği");
  sub(slide, "Aynı kaynak koddan hem web tarayıcısı hem masaüstü uygulama olarak çalışır.", 1.75);

  // Web kutusu
  card(slide, 0.9, 2.45, 5.3, 3.8);
  slide.addText("🌐", { x: 0.9, y: 2.7, w: 5.3, h: 1.0, fontSize: 44, align: "center" });
  slide.addText("Web Uygulaması", { x: 0.9, y: 3.8, w: 5.3, h: 0.5, fontSize: 18, color: C.textSub, bold: true, align: "center" });
  slide.addText("Next.js ile sunulan, tarayıcıdan\nerişilebilen responsive web arayüzü.\n\nlocalhost:3000", {
    x: 1.2, y: 4.35, w: 4.7, h: 1.5, fontSize: 12, color: C.textDim, align: "center",
  });

  // Masaüstü kutusu
  card(slide, 7.13, 2.45, 5.3, 3.8, C.card, C.navy);
  slide.addText("🖥️", { x: 7.13, y: 2.7, w: 5.3, h: 1.0, fontSize: 44, align: "center" });
  slide.addText("Masaüstü Uygulaması", { x: 7.13, y: 3.8, w: 5.3, h: 0.5, fontSize: 18, color: C.textSub, bold: true, align: "center" });
  slide.addText("Electron ile paketlenmiş, kurulum\ngerektirmeyen native desktop deneyimi.\n\nnpm run electron", {
    x: 7.43, y: 4.35, w: 4.7, h: 1.5, fontSize: 12, color: C.textDim, align: "center",
  });
}

/* ════════════════════════════════════════
   SLAYT 6 — ANALİZ MOTORU
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Analiz Motoru");
  title(slide, "Puanlama Sistemi");
  sub(slide, "Analiz 100 puan üzerinden başlar. Her tespit edilen sorun puanı düşürür.", 1.75);

  const rules = [
    { dot: C.yellow, sev: "Medium", sevColor: C.yellow, text: "Endpoint açıklaması eksik",                            pts: "−10", ptsColor: C.yellow },
    { dot: C.red,    sev: "High",   sevColor: C.red,    text: "Hata durum kodları (400/401/404) tanımlanmamış",       pts: "−15", ptsColor: C.red },
    { dot: C.red,    sev: "High",   sevColor: C.red,    text: "POST / PUT / DELETE'te kimlik doğrulama eksik",        pts: "−20", ptsColor: C.red },
    { dot: C.red,    sev: "High",   sevColor: C.red,    text: "DELETE + {id} → BOLA güvenlik riski",                  pts: "−15", ptsColor: C.red },
    { dot: C.yellow, sev: "Medium", sevColor: C.yellow, text: "GET metodu ile kalıcı işlem (REST standardına aykırı)", pts: "−10", ptsColor: C.yellow },
  ];

  rules.forEach((r, i) => {
    const y = 2.5 + i * 0.88;
    card(slide, 0.5, y, 12.33, 0.7);
    // dot
    slide.addShape(pptx.ShapeType.ellipse, { x: 0.75, y: y + 0.28, w: 0.14, h: 0.14, fill: { color: r.dot }, line: { color: r.dot } });
    // açıklama
    slide.addText(r.text, { x: 1.1, y: y + 0.17, w: 8.8, h: 0.38, fontSize: 13, color: C.textMuted });
    // severity badge
    slide.addShape(pptx.ShapeType.roundRect, { x: 9.9, y: y + 0.17, w: 1.2, h: 0.38, fill: { color: C.card }, line: { color: C.cardBorder, width: 0.5 }, rectRadius: 0.05 });
    slide.addText(r.sev, { x: 9.9, y: y + 0.17, w: 1.2, h: 0.38, fontSize: 10, color: r.sevColor, bold: true, align: "center", valign: "middle" });
    // puan
    slide.addText(r.pts + " puan", { x: 11.2, y: y + 0.17, w: 1.45, h: 0.38, fontSize: 13, color: r.ptsColor, bold: true, align: "right", valign: "middle" });
  });
}

/* ════════════════════════════════════════
   SLAYT 7 — YAPAY ZEKA
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Yapay Zeka");
  title(slide, "AI Entegrasyonu");
  sub(slide, "Eksik açıklamalar için yerel yapay zeka modeli otomatik Türkçe dokümantasyon üretir.", 1.75);

  const items = [
    { dot: C.blue,   text: "Ollama — internet bağlantısı gerektirmeyen yerel LLM çalıştırıcı" },
    { dot: C.blue,   text: "qwen2.5:7b — 7 milyar parametreli, Türkçe üretim için optimize model" },
    { dot: C.green,  text: "Kullanıcı \"AI ile Açıklama Üret\" butonuna tıklar" },
    { dot: C.green,  text: "Sistem HTTP metodu + endpoint path bilgisini modele iletir" },
    { dot: C.green,  text: "Model tek cümlelik Türkçe açıklama üretir" },
    { dot: C.green,  text: "Açıklama veritabanına kaydedilir, kalite uyarısı silinir, puan artar" },
  ];

  items.forEach((item, i) => {
    const y = 2.5 + i * 0.78;
    card(slide, 0.5, y, 12.33, 0.6);
    slide.addShape(pptx.ShapeType.ellipse, { x: 0.75, y: y + 0.23, w: 0.14, h: 0.14, fill: { color: item.dot }, line: { color: item.dot } });
    slide.addText(item.text, { x: 1.1, y: y + 0.12, w: 11.5, h: 0.38, fontSize: 13, color: C.textMuted });
  });
}

/* ════════════════════════════════════════
   SLAYT 8 — SDLC
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "SDLC");
  title(slide, "Yazılım Yaşam Döngüsü");

  const phases = [
    { icon: "📋", color: C.navy,      title: "Gereksinim\nAnalizi",   desc: "API kalite ve güvenlik\nsorunları tespit edildi.\nKullanıcı hikayeleri\nbelirlendi." },
    { icon: "🎨", color: C.navy,      title: "Sistem\nTasarımı",      desc: "Veritabanı şeması,\nAPI endpoint'leri ve\nfrontend sayfa yapısı\ntasarlandı." },
    { icon: "💻", color: C.greenDark, title: "İteratif\nGeliştirme",  desc: "Backend, frontend\nve AI entegrasyonu\naşamalı olarak\ngeliştirildi." },
    { icon: "🧪", color: C.greenDark, title: "Test ve\nDoğrulama",    desc: "Her modül test\nedildi. Güvenlik\nkuralları gerçek\nörneklerle doğrulandı." },
  ];

  phases.forEach((p, i) => {
    const x = 0.5 + i * 3.21;
    card(slide, x, 2.0, 3.0, 4.3, C.card, p.color);
    slide.addShape(pptx.ShapeType.roundRect, { x: x + 1.05, y: 2.25, w: 0.9, h: 0.9, fill: { color: p.color }, line: { color: p.color }, rectRadius: 0.1 });
    slide.addText(p.icon, { x: x + 1.05, y: 2.25, w: 0.9, h: 0.9, fontSize: 24, align: "center", valign: "middle" });
    slide.addText(p.title, { x: x + 0.1, y: 3.3, w: 2.8, h: 0.65, fontSize: 14, color: C.textSub, bold: true, align: "center" });
    slide.addText(p.desc, { x: x + 0.1, y: 4.05, w: 2.8, h: 1.8, fontSize: 11.5, color: C.textDim, align: "center" });
  });
}

/* ════════════════════════════════════════
   SLAYT 9 — BM SKA
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "BM Sürdürülebilir Kalkınma Amaçları");
  title(slide, "SDG 9 ile Örtüşme");

  // Ana kutu
  card(slide, 1.5, 2.0, 10.33, 4.3, "0f172a", "1e3a5f");

  // Büyük numara
  slide.addText("09", { x: 1.8, y: 2.2, w: 2.0, h: 1.4, fontSize: 80, color: C.accentMid, bold: true, align: "center" });

  // Başlık
  slide.addText("Sanayi, Yenilikçilik ve Altyapı", { x: 3.9, y: 2.3, w: 7.5, h: 0.55, fontSize: 20, color: C.blueLight, bold: true });

  divider(slide, 3.05);

  slide.addText(
    "API Insight Studio, yazılım geliştirme süreçlerini otomatize ederek API kalitesini\n" +
    "ve güvenliğini artırmaktadır. Daha güvenilir ve iyi belgelenmiş API'ler, daha sağlam\n" +
    "dijital altyapıların oluşturulmasına katkı sağlar.\n\n" +
    "Yapay zeka ile dokümantasyon otomasyonu, küçük ekiplerin bile endüstri\n" +
    "standartlarına ulaşmasını mümkün kılar — bu da teknoloji alanındaki\n" +
    "fırsat eşitliğine katkıda bulunur.",
    { x: 1.8, y: 3.15, w: 9.8, h: 2.8, fontSize: 13, color: C.textDim, lineSpacingMultiple: 1.4 }
  );
}

/* ════════════════════════════════════════
   SLAYT 10 — KAPSANAN KONULAR
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Kapsanan Konular");
  title(slide, "Ders Gereksinimlerini Karşılayan Alanlar");

  const topics = [
    { icon: "🤖", color: C.navy,       tColor: "93c5fd", title: "Yapay Zeka",       desc: "Ollama + qwen2.5:7b\nYerel LLM entegrasyonu\nOtomatik dokümantasyon" },
    { icon: "🌐", color: C.greenDark,  tColor: "86efac", title: "Web Servisleri",   desc: "REST API backend\nJWT kimlik doğrulama\nOpenAPI standartları" },
    { icon: "🔄", color: C.purpleDark, tColor: "d8b4fe", title: "Dijital Dönüşüm", desc: "Manuel süreçlerin\notomasyona taşınması\nYazılım kalitesi artışı" },
  ];

  topics.forEach((t, i) => {
    const x = 0.5 + i * 4.28;
    card(slide, x, 2.0, 4.05, 3.3, C.card, t.color);
    slide.addShape(pptx.ShapeType.roundRect, { x: x + 1.55, y: 2.2, w: 0.95, h: 0.95, fill: { color: t.color }, line: { color: t.color }, rectRadius: 0.1 });
    slide.addText(t.icon, { x: x + 1.55, y: 2.2, w: 0.95, h: 0.95, fontSize: 26, align: "center", valign: "middle" });
    slide.addText(t.title, { x: x + 0.15, y: 3.25, w: 3.75, h: 0.45, fontSize: 15, color: t.tColor, bold: true, align: "center" });
    slide.addText(t.desc, { x: x + 0.15, y: 3.75, w: 3.75, h: 1.2, fontSize: 12, color: C.textDim, align: "center" });
  });

  // Zorunlu gereksinimler
  card(slide, 0.5, 5.5, 5.9, 0.8);
  slide.addText("✅  Çoklu Platform", { x: 0.7, y: 5.55, w: 2.8, h: 0.38, fontSize: 13, color: C.textSub, bold: true });
  slide.addText("Web (Next.js) + Masaüstü (Electron)", { x: 0.7, y: 5.9, w: 5.5, h: 0.3, fontSize: 11, color: C.textDim });

  card(slide, 6.93, 5.5, 5.9, 0.8);
  slide.addText("✅  Veritabanı", { x: 7.13, y: 5.55, w: 2.8, h: 0.38, fontSize: 13, color: C.textSub, bold: true });
  slide.addText("MSSQL + Entity Framework Core", { x: 7.13, y: 5.9, w: 5.5, h: 0.3, fontSize: 11, color: C.textDim });
}

/* ════════════════════════════════════════
   SLAYT 11 — SONUÇ
════════════════════════════════════════ */
{
  const slide = S();
  tag(slide, "Sonuç");
  title(slide, "Özet");
  sub(slide, "API Insight Studio, tek bir Swagger dosyasıyla eksiksiz bir API kalite raporu sunar.", 1.75);

  const stats = [
    { num: "2",   label: "Platform\n(Web + Masaüstü)" },
    { num: "5",   label: "Analiz\nKuralı" },
    { num: "AI",  label: "Otomatik\nDokümantasyon" },
    { num: "SDG", label: "BM Hedef 9\nUyumu" },
  ];

  stats.forEach((s, i) => {
    const x = 1.2 + i * 2.8;
    card(slide, x, 2.6, 2.35, 2.0);
    slide.addText(s.num, { x, y: 2.7, w: 2.35, h: 0.9, fontSize: 36, color: C.blue, bold: true, align: "center" });
    slide.addText(s.label, { x, y: 3.65, w: 2.35, h: 0.7, fontSize: 11, color: C.textDim, align: "center" });
  });

  // Teşekkürler
  slide.addText("Teşekkürler", { x: 0.5, y: 5.1, w: 12.33, h: 0.8, fontSize: 34, color: C.blue, bold: true, align: "center" });
  slide.addText("Sorularınızı bekliyorum.", { x: 0.5, y: 5.95, w: 12.33, h: 0.4, fontSize: 14, color: C.textDim, align: "center" });
}

/* ════════════════════════════════════════
   KAYDET
════════════════════════════════════════ */
pptx.writeFile({ fileName: "API_Insight_Studio_Sunum.pptx" }).then(() => {
  console.log("✅  API_Insight_Studio_Sunum.pptx oluşturuldu.");
}).catch((err) => {
  console.error("❌  Hata:", err);
});
