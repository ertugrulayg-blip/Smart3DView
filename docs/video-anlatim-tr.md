# Smart3DView — Revit eklentisi video anlatımı (TR)

Süre: yaklaşık 8–9 dakika · Sürüm 1.16.2 · Yalnız Revit tarafı (Web'e aktar anlatılmaz).
Her sahnede **Ekranda** ne gösterileceği, **Anlatım** okunacak metin.
Hazırlık: bir MEP modeli + en az bir bağlı model (mimari/taşıyıcı), Revit 2025/2026/2027, eklenti kurulu, ikinci ekran varsa açık.

---

## 1. Açılış (0:00 – 0:30)

**Ekranda:** Smart3DView logosu, ardından renkli bir tesisat bölgesinin 3B görüntüsü yavaşça dönüyor.

**Anlatım:**
Hoş geldiniz! Bu videoda Revit için geliştirdiğimiz Smart3DView eklentisini baştan sona tanıtacağız.
Smart3DView, Revit modelinizin üzerinde çalıştığınız bölümünü kendi hızlı 3B penceresinde açar. Revit'in 3B görünümünü yormadan, takılmadan döndürür, keser, ölçer ve çakışmaları görürsünüz. Üstelik bu pencereyi ikinci ekranınızda açık tutup Revit'te çalışmaya devam edebilirsiniz.

---

## 2. Kurulum ve ilk açılış (0:30 – 1:10)

**Ekranda:** İndirilen ZIP, içindeki yıl klasörü, `%AppData%\Autodesk\Revit\Addins\2026` klasörüne kopyalama; Revit açılırken “Always Load”; Eklentiler sekmesindeki Smart3DView paneli.

**Anlatım:**
Kurulum çok basit; ayrı bir kurulum programı yok. İndirdiğiniz dosyada Revit sürümünüzün klasörünü açın, içindeki iki öğeyi Revit'in Addins klasörüne kopyalayın.
Revit'i açtığınızda imzasız eklenti uyarısı çıkarsa “Her zaman yükle”yi seçin.
Artık Eklentiler sekmesinde Smart3DView paneli ve Smart 3D View düğmesi hazır. İlk 14 gün tüm özellikler açık, hiçbir şey girmeniz gerekmiyor.

---

## 3. Neyi açacağınızı seçmek (1:10 – 2:10)

**Ekranda:** (a) Planda birkaç boru ve kanal seçilip düğmeye basılıyor → pencere açılıyor. (b) Kesit kutusu açık bir 3B görünümde seçim yokken düğme → o kutu açılıyor. (c) Plan görünümünde seçim yokken düğme → dikdörtgen çiziliyor.

**Anlatım:**
Smart3DView ne açacağını bulunduğunuz yere göre anlar.
Birkaç eleman seçip düğmeye basarsanız, seçiminizin çevresi her yönde otuz santim payla açılır.
Hiçbir şey seçili değilken kesit kutusu açık bir 3B görünümdeyseniz, o kutu açılır.
Plan görünümündeyseniz bir dikdörtgen çizersiniz; yükseklik planın görünüm aralığından alınır.
Bağlı modeller de otomatik gelir; 3B görünümde gizlediğiniz kategoriler gizli kalır.
Şeritteki düğmeye her basışınızda yeni bir pencere açılır; birden çok bölgeyi yan yana açık tutabilirsiniz.

---

## 4. Gezinme (2:10 – 2:50)

**Ekranda:** Shift + orta tuşla döndürme, orta tuşla kaydırma, tekerlekle imlece doğru yakınlaşma, çift tık ile sığdırma, Home.

**Anlatım:**
Gezinme Revit'teki gibi: Shift ile orta tuşu basılı tutup sürüklerseniz döndürür, yalnız orta tuşla kaydırırsınız. Tekerlek imlecin olduğu noktaya doğru yakınlaşır.
Bir elemanı seçtiyseniz dönüş onun etrafında yapılır.
Çift tıklarsanız ya da F'ye basarsanız seçili eleman ya da tüm alan ekrana sığar; Home ile varsayılan 3B açıya dönersiniz.
Sağ üstteki görünüm küpüne tıklayarak tam önden, yandan ya da üstten bakabilirsiniz.

---

## 5. Tonlar: Detaylı, Renkli, Kağıt (2:50 – 3:50)

**Ekranda:** Sağ alttaki yuvarlak düğmeler; 1–7 tuşlarıyla geçiş. Detaylı → Renkli (sol üstte model renkleri) → Kağıt (kalem eskizi) → gri tonlar.

**Anlatım:**
Sağ alttaki yuvarlak düğmeler, ya da 1'den 7'ye kadar tuşlar, görünümü değiştirir.
Varsayılan Detaylı tonda her eleman türü kendi rengindedir: duvarlar gri, kablo tavaları metalik, kanallar galvaniz, mekanik cihazlar mor, elektrik panoları turuncu, aydınlatma sarı, sprinkler'lar kırmızı. Kalabalık bir tavan arasında hangi şeyin ne olduğunu bir bakışta ayırırsınız.
Renkli tonda ise ana model ve her bağlı model kendi pastel rengini alır; sol üstte hangi rengin hangi dosya olduğu yazar. Bir elemanın hangi modelden geldiğini aramanıza gerek kalmaz.
Kağıt tonu ise buz beyazı zemin üzerinde elle çizilmiş bir kalem eskizi gibi görünür; sunum ve paftalar için çok şık.
Beyaz, açık gri, gri ve koyu tonlar da sade bir görünüm için hazır. Kesit kutusunun kestiği yüzler her tonda dolu poşe olarak çizilir.

---

## 6. Kesit kutusunu düzenleme (3:50 – 4:50)

**Ekranda:** Kutu düğmesi (B); üstünde açılan şerit (Taşı, ↺); mavi tutamaç sürükleme; Ctrl ile bütün kutuyu taşıma; kutuyu dışarı büyütünce durum çubuğunda yüzde ve Esc ile iptal.

**Anlatım:**
Kutu düğmesine ya da B'ye basın; kutunun çizgileri ve altı mavi tutamaç görünür. Bir tutamacı sürükleyerek o yüzü içeri ya da dışarı alırsınız, kesit anında güncellenir.
Kutu düğmesinin üstünde açılan küçük şeritteki Taşı ile, ya da sürüklerken Ctrl'ye basılı tutarak, bütün kutuyu kaydırırsınız. Geri ok kutuyu başa döndürür.
Kutuyu ilk okunan alanın dışına büyütürseniz Smart3DView Revit'ten yalnız yeni giren elemanları okur. Okuma parça parça yapılır; bu sırada Revit donmaz, durum çubuğunda yüzde görürsünüz, Esc'ye basarak istediğiniz an iptal edebilirsiniz.

---

## 7. Çakışma denetimi (4:50 – 6:00)

**Ekranda:** Çakışma (C) → kırmızı/mavi elemanlar, diğerleri soluk; tolerans düğmesi 5 → 10 → 25 → 50 mm → dokunma; çakışan bir boruya tıklama → altta neyle çakıştığı; model listesinden bir modeli gizleme.

**Anlatım:**
Şimdi en sevdiğimiz özelliklerden biri: çakışma denetimi. Çakışma düğmesine ya da C'ye basın.
Farklı tesisatlar arasındaki çakışmalar iki renkte görünür: bir taraf kırmızı, diğer taraf mavi. Geri kalan her şey soluklaşır, böylece sorunlu yerler hemen göze çarpar.
Çakışan bir elemana tıklayın: kendisi kırmızı, ona giren elemanlar mavi olur ve altta neyle çakıştığı yazar.
Yanındaki tolerans düğmesi, iki elemanın çakışma sayılması için birbirinin içine ne kadar girmesi gerektiğini belirler. Varsayılan beş milimetre. Böylece döşemeye oturan bir cihaz ya da duvara monte bir pano boşuna çakışma olarak çıkmaz. Tıkladıkça on, yirmi beş, elli milimetre ya da “dokunma da sayılsın” seçilir.
Akıllı kurallar var: bir boru kendi dirseği ya da kapliniyle, bir kanal kendi izolasyonuyla, connector ile bağlı elemanlar birbiriyle çakışma sayılmaz. Bağlı modellerdeki elemanlar da denetlenir; sol üstteki listeden gizlediğiniz modeller denetime girmez.

---

## 8. Ölç (6:00 – 6:50)

**Ekranda:** Ölç (D); imlecin köşe ✕, kenar ortası ○, boru ekseni ◉ işaretleri; iki nokta; Z kilidiyle boru ekseni – kanal altı düşey mesafe; Backspace / Sil.

**Anlatım:**
Ölç düğmesine ya da D'ye basın ve iki nokta tıklayın. İmleç AutoCAD'deki gibi yakalar: köşe, kenar ortası, boru ve kanal ekseni, kenar ya da yüzey; işaretin şekli neyi tuttuğunu gösterir.
Mesafe çizginin üstünde, X, Y, Z bileşenleri durum çubuğunda, projenizin uzunluk biriminde yazar.
X, Y ya da Z tuşuna basarak ölçüyü o eksene kilitlersiniz. Örneğin Z ile boru ekseniyle kanal altı arasındaki düşey mesafeyi tek hamlede bulursunuz.
Backspace son ölçüyü, Sil hepsini kaldırır. Ölçüler ekranda kalır ve alacağınız görüntüye de girer.

---

## 9. Etiket, Revit'te göster, Yenile (6:50 – 7:30)

**Ekranda:** T ile imlecin yanında kategori/tip; bir eleman seçip “Revit'te göster” → Revit'te seçili; Revit'te bir kanalı taşıma → Yenile düğmesi turuncu → R ile yenileme, kamera ve kutu yerinde.

**Anlatım:**
T tuşuyla etiketi açın: imleci bir elemanın üzerinde gezdirdiğinizde kategorisi ve tipi hemen yanında yazar.
Bir eleman seçip Revit'te göster'e tıklarsanız eleman Revit'te seçilir ve ekrana getirilir.
Revit'te modeli değiştirdiğinizde Yenile düğmesi turuncu olur. R'ye ya da F5'e basın; aynı alan yeniden okunur. Kameranız, kutunuz, tonunuz ve çakışma ayarınız olduğu gibi kalır.

---

## 10. Görüntü al (7:30 – 8:00)

**Ekranda:** Görüntü al (P) → kaydedildi penceresi → Proje Tarayıcısı → Renderings → “Smart3DView - tarih saat” → paftaya sürükleme.

**Anlatım:**
Görüntü al'a ya da P'ye bastığınızda Smart3DView ekranı yaklaşık 4K çözünürlükte çizer ve doğrudan modelinize kaydeder. Proje Tarayıcısı'nda Renderings altında bulursunuz; herhangi bir render gibi paftaya yerleştirebilirsiniz. PNG kopyası da bilgisayarınızda kalır.

---

## 11. Deneme ve lisans (8:00 – 8:30)

**Ekranda:** Pencerenin altındaki “Deneme” düğmesi → lisans penceresi → anahtar yapıştırma → Etkinleştir.

**Anlatım:**
İlk on dört gün her şey açık. Deneme bittikten sonra gri tonlarda 3B görüntüleme ücretsiz kalmaya devam eder.
Detaylı ve Renkli tonlar, çakışma, ölçü, kutu düzenleme ve görüntü alma için tam lisans tek seferlik dört dolar; abonelik yok ve iki bilgisayarda kullanabilirsiniz.
Anahtarınız satın alma e-postasıyla gelir. Pencerenin altındaki lisans düğmesine tıklayıp anahtarı yapıştırmanız yeterli.

---

## 12. Kapanış (8:30 – 8:50)

**Ekranda:** Kağıt tonunda dönen bir bölge; ekranda F1 ve schema-tools.net/smart3dview yazısı.

**Anlatım:**
Takıldığınız bir yerde pencerede F1'e basarak yardım sayfasını açabilirsiniz. Görüş ve önerilerinizi bekliyoruz.
Hepsi bu kadar — izlediğiniz için teşekkürler!

---

### Çekim notları
- Kısayolları ekranda küçük bir etiketle gösterin (ör. “C — Çakışma”).
- Çakışma sahnesinde boru–kanal–tava kesişen dolu bir tavan arası seçin; tolerans farkını göstermek için döşemeye oturan bir cihaz bulunsun.
- Renkli ton için en az iki bağlı model (mimari + taşıyıcı) olsun ki pastel renkler ayrışsın.
- Revit'in donmadığını göstermek için kutuyu büyütürken Revit penceresinde de bir şey seçin.
