# AI açıklaması biçim değerlendirmesi

Bu dosya `eval/AiEval` aracıyla üretilir; elle düzenlenmez. Ham çıktılar `ai-outputs.json` içindedir.

- Ölçüm zamanı (UTC): 2026-10-04 21:47
- Donanım: Intel Core i5-10300H, 16 GB RAM, NVIDIA GTX 1650 Ti 4 GB, Windows 11, Ollama 0.35.1
- 18 benzersiz endpoint × 3 tekrar = model başına 54 örnek (her modelde aynı komut ve aynı uç noktalar; model örnekleme yaptığı için tekrarlar farklı çıkabilir)
- Süre: istek başına duvar saati; ilk (ısınma) istek hariç, model belleğe yüklendikten sonra

**Bu bir BİÇİM ölçümüdür, anlam doğruluğu değil.** Ölçütler, istenen biçime uyumu kontrol eder (aşağıda). "Yöntemle tutarlı eylem" ölçütü, açıklamada metoda uygun bir fiilin (GET için listeler/getirir, DELETE için siler, vb.) geçip geçmediğine bakan kaba bir vekil ölçüttür; cümlenin gerçekten doğru olduğunu kanıtlamaz.

## Sonuç

| Model | Hepsi geçti | Boş değil | Tek cümle | Yasaklı içerik yok | Türkçe | Uzunluk | Yöntemle tutarlı eylem | İstek hatası | Ort. süre | Medyan | En yavaş |
|---|---|---|---|---|---|---|---|---|---|---|---|
| qwen2.5:7b | %74 (40/54) | %100 | %96 | %100 | %98 | %100 | %76 | 0 | 4,1 sn | 4,1 sn | 7,1 sn |
| qwen2.5:3b | %80 (43/54) | %100 | %96 | %100 | %96 | %100 | %81 | 0 | 0,6 sn | 0,6 sn | 1,3 sn |

## Ölçütler

- **Boş değil**: kırpılmış cevap boş değil.
- **Tek cümle**: satır sonu yok, cümle sınırı yok, noktalama ile bitiyor.
- **Yasaklı içerik yok**: komutun yasakladığı şeyler yok (backtick, URL, "Örneğin", `GET /...` biçiminde istek satırı). Yol parametresinin (`{id}`) cümlede aynen yazılması yasaklı sayılmaz.
- **Türkçe**: Türkçe'ye özgü harf veya sık Türkçe sözcük sayısı, sık İngilizce sözcük sayısından az değil (basit sezgi) ve Latin dışı yazı (Çince, Kiril, Arapça vb.) yok.
- **Uzunluk**: 20-250 karakter.
- **Yöntemle tutarlı eylem**: metoda uygun bir fiil kökü geçiyor (kaba vekil ölçüt).

## qwen2.5:7b: ölçütleri geçemeyenler (14/54)

- `GET /items`: yöntemle tutarsız. Çıktı: "'/items' endpointi, kullanıcılarına kayıtlı ürünlerin listesini sağlar."
- `POST /items`: yöntemle tutarsız. Çıktı: "'/items' endpointi, yeni ürün kaydı için kullanılır."
- `DELETE /items/{itemId}`: tek cümle değil, Türkçe değil, yöntemle tutarsız. Çıktı: "Bu endpoint, belirli birgetItemId的商品项进行删除。"
- `DELETE /items/{itemId}`: tek cümle değil. Çıktı: "'{itemId} olarak belirtilen öğeyi siler.'"
- `GET /items/delete`: yöntemle tutarsız. Çıktı: "Bu endpoint, belirtilen ürünün silinmesini sağlar."
- `GET /items/delete`: yöntemle tutarsız. Çıktı: "'/items/delete' endpointi, belirli bir ürünün silinmesini sağlar."
- `GET /users/update`: yöntemle tutarsız. Çıktı: "Kullanıcı bilgilerini güncelleme amacıyla API çağırmak."
- `GET /users/update`: yöntemle tutarsız. Çıktı: "Bu endpoint, kullanıcı bilgilerinin güncellenmesi için kullanılır."
- `GET /users/update`: yöntemle tutarsız. Çıktı: "Kullanıcı bilgilerinin güncellenmesi için kullanılır."
- `GET /cache/remove`: yöntemle tutarsız. Çıktı: "'/cache/remove' endpointi, önbellek öğesinin silinmesini sağlar."
- `GET /updates`: yöntemle tutarsız. Çıktı: "'/updates' endpointi, kullanıcılarına en son güncellemeleri sunar."
- `PATCH /items/{itemId}`: yöntemle tutarsız. Çıktı: "'/items/{itemId}' endpointi, belirli bir öğeyi parçalaya@update ederek sunar."
- `GET /items/{itemId}/activate`: yöntemle tutarsız. Çıktı: "'items' koleksiyonunda belirli bir öğeyi aktiflestirmek için kullanılır."
- `GET /items/{itemId}/activate`: yöntemle tutarsız. Çıktı: "Bu endpoint, belirli bir öğeyi etkinleştirmek için kullanılır."

## qwen2.5:3b: ölçütleri geçemeyenler (11/54)

- `POST /items`: yöntemle tutarsız. Çıktı: "'/items' endpoint, verilen ürün bilgilerinin kaydedilmesini sağlar."
- `DELETE /items/{id}`: yöntemle tutarsız. Çıktı: "HTTP DELETE '/items/{id}' endpointi işlevsiz hale getirir."
- `DELETE /items/{itemId}`: tek cümle değil, Türkçe değil, yöntemle tutarsız. Çıktı: "'/items/{itemId}' endpointi通过 HTTP DELETE 메소드 호출led时用于删除指定ID的项目。"
- `GET /items/delete`: yöntemle tutarsız. Çıktı: "'/items/delete' endpointi silinecek kaynakları silmek için kullanıluyor."
- `GET /items/delete`: yöntemle tutarsız. Çıktı: "'/items/delete' endpointi silinecek kayıtlarını silmeye olanak sağlar."
- `GET /users/update`: yöntemle tutarsız. Çıktı: "'/users/update' endpoint, kullanıcıyı güncellemek için kullanılır."
- `GET /users/update`: yöntemle tutarsız. Çıktı: "'/users/update' endpoint, kendi bilgilerini güncellemek için kullanılır."
- `GET /cache/remove`: yöntemle tutarsız. Çıktı: "'/cache/remove' endpointi eksik bellekleme kaynaklarını silmektedir."
- `GET /cache/remove`: yöntemle tutarsız. Çıktı: "/http cache silinmesini sağlar."
- `GET /updates`: tek cümle değil, Türkçe değil. Çıktı: "'/updates' endpointi güncel bilgileri ile ilgili.GET isteği yapısına dayalı返ユア。"
- `GET /items/{itemId}/activate`: yöntemle tutarsız. Çıktı: "'/items/{itemId}/activate' endpointi aktif kategorilere tanınan ürünleri etkinleştirme amaçlı oluşturulmuştur."

