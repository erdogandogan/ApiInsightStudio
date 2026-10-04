# Kural motoru değerlendirmesi

Bu dosya `EVAL_WRITE_REPORT=1 dotnet test --filter Category=Eval` ile üretilir; elle düzenlenmez.
Her örnek gerçek yükleme + analiz hattından (ayrıştırma, kurallar, skor) geçirilir.

## Özet

- Örnek sayısı: **29**
- **Kural tanımına uyum** (elle hesaplanan skor ve bulgular): **29/29**
- **Gerçek dünya uyumu** (ideal sonuçla birebir): **23/29**
- Bulgu düzeyinde, ideal sonuca göre: kesinlik **%91,4** (32 doğru, 3 yanlış pozitif), duyarlılık **%91,4** (3 kaçan bulgu)

"Kural tanımına uyum" motorun yazıldığı gibi çalıştığını (regresyon), "gerçek dünya uyumu" ise kuralların kendisinin ne kadar isabetli olduğunu ölçer. İkisi bilerek ayrıdır: aşağıdaki sınırlar kuralların bilinen zayıflıklarıdır, hata değil.

## Örnekler

| # | Örnek | Endpoint | Beklenen skor | Skor | Kural uyumu | Gerçek dünya |
|---|---|---|---|---|---|---|
| 1 | temiz-get | 1 | 100 | 100 | ✓ | ✓ |
| 2 | temiz-korumali-crud | 3 | 100 | 100 | ✓ | ✓ |
| 3 | aciklama-eksik | 1 | 90 | 90 | ✓ | ✓ |
| 4 | bosluk-aciklama | 1 | 90 | 90 | ✓ | ✓ |
| 5 | sadece-description | 1 | 100 | 100 | ✓ | ✓ |
| 6 | hata-kodu-eksik | 1 | 85 | 85 | ✓ | ✓ |
| 7 | iki-eksik | 1 | 75 | 75 | ✓ | ✓ |
| 8 | sadece-401 | 1 | 100 | 100 | ✓ | ✓ |
| 9 | sadece-404 | 1 | 100 | 100 | ✓ | ✓ |
| 10 | post-kimliksiz | 1 | 80 | 80 | ✓ | ✓ |
| 11 | put-kimliksiz | 1 | 80 | 80 | ✓ | ✓ |
| 12 | delete-kimliksiz-idsiz | 1 | 80 | 80 | ✓ | ✓ |
| 13 | genel-guvenlik | 1 | 100 | 100 | ✓ | ✓ |
| 14 | endpoint-guvenligi | 1 | 100 | 100 | ✓ | ✓ |
| 15 | delete-id-korumali | 1 | 85 | 85 | ✓ | ✓ |
| 16 | delete-id-kimliksiz | 1 | 65 | 65 | ✓ | ✓ |
| 17 | delete-itemid | 1 | 100 | 100 | ✓ | ✗ (ideal 85) |
| 18 | get-delete-yolu | 1 | 90 | 90 | ✓ | ✓ |
| 19 | get-update-yolu | 1 | 90 | 90 | ✓ | ✓ |
| 20 | get-remove-yolu | 1 | 90 | 90 | ✓ | ✓ |
| 21 | get-updates-akisi | 1 | 90 | 90 | ✓ | ✗ (ideal 100) |
| 22 | herkese-acik-login | 1 | 80 | 80 | ✓ | ✗ (ideal 100) |
| 23 | sadece-422 | 1 | 85 | 85 | ✓ | ✗ (ideal 100) |
| 24 | patch-kimliksiz | 1 | 100 | 100 | ✓ | ✗ (ideal 80) |
| 25 | get-activate-yolu | 1 | 100 | 100 | ✓ | ✗ (ideal 90) |
| 26 | karma-coklu | 3 | 5 | 5 | ✓ | ✓ |
| 27 | sifirin-altina-inmez | 3 | 0 | 0 | ✓ | ✓ |
| 28 | bos-dokuman | 0 | 100 | 100 | ✓ | ✓ |
| 29 | swagger-2 | 1 | 100 | 100 | ✓ | ✓ |

## Bilinen sınırlar

- **delete-itemid** (yanlış negatif): Kural yalnızca '{id}' adlı parametreyi tanır; '{itemId}' gibi adlar kaçar (yanlış negatif).
- **get-updates-akisi** (yanlış pozitif): Yol alt dizgisi eşleşmesi 'updates' gibi masum adları da işaretler (yanlış pozitif).
- **herkese-acik-login** (yanlış pozitif): Giriş uç noktası bilerek kimliksizdir; kural herkese açık amacı ayırt edemez (yanlış pozitif).
- **sadece-422** (yanlış pozitif): Yalnızca 400/401/404 hata kodu sayılır; 422 tanımlı olsa da eksik görünür (yanlış pozitif).
- **patch-kimliksiz** (yanlış negatif): Yalnızca POST/PUT/DELETE kritik sayılır; PATCH kaçar (yanlış negatif).
- **get-activate-yolu** (yanlış negatif): Üç sözcüklük liste dışındaki fiiller kaçar (yanlış negatif).
