using UploaderCore;

namespace Uploader;

/// <summary>Arayüz metinlerinin İngilizce karşılıkları (kaynak dil Türkçe; anahtar metnin kendisi).</summary>
internal static class Strings
{
    public static void Register() => L.Register(new Dictionary<string, string>
    {
        ["Maç"] = "Match", ["Kadrolar"] = "Rosters", ["Fotoğraflar"] = "Photos", ["Yükleme"] = "Upload",
        ["Forma numarasından oyuncuya, oyuncudan kategoriye"] = "From jersey number to player, from player to category",
        ["Maç fotoğraflarınızı Commons'a taşıyın"] = "Bring your match photos to Commons",
        ["Bir maçın fotoğraflarını içeren klasörü seçin. Forma numaraları bu bilgisayarda okunur, oyuncular kadrodan bulunur ve Commons kategorileri önerilir."] =
            "Choose the folder that holds the photos of one match. Jersey numbers are read on this computer, players are looked up in the roster and Commons categories are suggested.",
        ["Klasör seç"] = "Choose folder", ["Klasör değiştir"] = "Change folder", ["Durdur"] = "Stop", ["Tamam"] = "OK",
        ["Bir sorun oluştu"] = "Something went wrong",
        ["Maç fotoğraflarının bulunduğu klasörü seçin"] = "Choose the folder that contains the match photos",
        ["{0} fotoğraf"] = "{0} photos",
        ["Proje dosyası okunamadı: {0}"] = "Could not read the project file: {0}",
        ["Kadro getirilemedi: {0}"] = "Could not fetch the roster: {0}",
        ["Analiz başlatılamadı: {0}"] = "Could not start the analysis: {0}",
        ["Denetim yapılamadı: {0}"] = "Could not run the check: {0}",
        ["Arama yapılamadı: {0}"] = "Search failed: {0}",
        // Maç
        ["Maç ve yükleme bilgileri"] = "Match and upload details",
        ["Maç başlığı"] = "Match title", ["Dosya adında ve açıklamada kullanılır"] = "Used in file names and descriptions",
        ["Maç kategorisi"] = "Match category", ["Boşsa takım kategorileri kullanılır"] = "Team categories are used when empty",
        ["Maç kategorisi yoksa oluştur"] = "Create the match category if missing",
        ["Commons'ta yoksa takım kategorilerinin altında oluşturulur"] = "Created under the team categories if it does not exist on Commons",
        ["Diğer kategoriler (virgülle)"] = "Other categories (comma separated)",
        ["Dosya adı şablonu"] = "File name template",
        ["Alanlar: {oyuncular} {numaralar} {maç} {sıra}"] = "Fields: {players} {numbers} {match} {n}",
        ["Yazar"] = "Author", ["Boşsa Commons kullanıcı adınız"] = "Your Commons user name when empty",
        ["Lisans"] = "License", ["CC0 (kamu malı)"] = "CC0 (public domain)",
        ["{0}. takım"] = "Team {0}", ["Takım ara (ör. Fenerbahçe basketbol)"] = "Search for a team (e.g. Fenerbahçe basketball)",
        ["Ara"] = "Search", ["Sonuç yok"] = "No results",
        ["{0} oyuncu · {1}"] = "{0} players · {1}", ["Commons kategorisi yok"] = "no Commons category",
        ["Bu maçtaki forma renkleri"] = "Jersey colours in this match",
        ["Formadaki yazılar (takım adı, sponsor; virgülle)"] = "Text on the jersey (team name, sponsor; comma separated)",
        ["Takımın Commons kategorisi"] = "Commons category of the team",
        // Kadrolar
        ["Takım seçilmedi"] = "No team selected", ["Vikipedi'den yenile"] = "Refresh from Wikipedia", ["Oyuncu ekle"] = "Add player",
        ["Kaynak: {0}.wikipedia.org — {1}. Maç günündeki kadroyla karşılaştırıp düzeltin."] = "Source: {0}.wikipedia.org — {1}. Compare with the match-day squad and correct it.",
        ["Kadro boş. \"Maç\" adımından takımı seçin ya da oyuncuları elle ekleyin."] = "The roster is empty. Pick the team in the \"Match\" step or add players by hand.",
        ["No"] = "No.", ["Ad Soyad"] = "Full name", ["Commons kategorisi"] = "Commons category",
        // Fotoğraflar
        ["Tümü"] = "All", ["Oyuncu bulundu"] = "Player found", ["Çözülemeyen numara"] = "Unresolved number", ["Oyuncu yok"] = "No player", ["Analiz edilmedi"] = "Not analysed",
        ["Analiz et ({0})"] = "Analyse ({0})", ["Tümü analiz edildi"] = "All analysed",
        ["Oyuncu bulunamadı"] = "No player found", ["Analiz bekliyor"] = "Waiting for analysis",
        ["Yüklendi"] = "Uploaded", ["Hata"] = "Error", ["Bu fotoğrafı yükle"] = "Upload this photo",
        ["Commons dosya adı"] = "Commons file name", ["Şablondaki ada dön"] = "Revert to the template name",
        ["Kategoriler"] = "Categories", ["Kategori yok: maç ya da takım kategorisi girin."] = "No category: enter a match or team category.",
        ["Analiz hatası: {0}"] = "Analysis error: {0}", ["Yeniden analiz et"] = "Analyse again",
        ["Ayrıntı için bir fotoğraf seçin."] = "Select a photo to see the details.",
        ["Oyuncular (elle düzenlendi)"] = "Players (edited by hand)", ["Oyuncular"] = "Players", ["Otomatiğe dön"] = "Back to automatic",
        ["güven %{0}"] = "confidence {0}%", ["kategorisi yok"] = "no category", ["Ekle"] = "Add", ["Kaldır"] = "Remove",
        // Yükleme
        ["yüklenecek"] = "to upload", ["yüklendi"] = "uploaded", ["hatalı"] = "failed", ["hariç tutuldu"] = "excluded",
        ["Kategorileri ve dosya adlarını denetle"] = "Check categories and file names",
        ["{0} fotoğrafı yükle"] = "Upload {0} photos", ["Örnek sayfa metni: {0}"] = "Sample page text: {0}", ["Sonuçlar"] = "Results",
        ["{0} fotoğraf {1} hesabıyla Wikimedia Commons'a herkese açık olarak yüklenecek."] = "{0} photos will be uploaded publicly to Wikimedia Commons with the account {1}.",
        ["Yükle"] = "Upload", ["Vazgeç"] = "Cancel",
        ["Yüklenen dosyalar seçtiğiniz özgür lisansla yayımlanır; bu işlem geri alınamaz."] = "Uploaded files are published under the free licence you chose; this cannot be undone.",
        ["Tüm kategoriler Commons'ta mevcut."] = "All categories exist on Commons.",
        ["Commons'ta bulunmayan kategoriler ({0}):"] = "Categories missing on Commons ({0}):",
        ["Dosya adları kullanılabilir."] = "The file names are available.",
        ["Bu adlarla dosya zaten var ({0}):"] = "Files with these names already exist ({0}):",
        ["Commons oturumu"] = "Commons login", ["Commons oturumu: {0}"] = "Commons account: {0}", ["Oturumu kapat"] = "Log out",
        ["Oturum açılmadı"] = "Not logged in",
        ["Wikimedia hesabınızla oturum açın. Parolanız uygulamaya verilmez; yalnızca dosya yükleme ve sayfa düzenleme izni istenir."] =
            "Log in with your Wikimedia account. The app never sees your password; it only asks for permission to upload files and edit pages.",
        ["Commons ile giriş yap"] = "Log in with Commons",
        ["Tarayıcıda izin verin; bu pencere kendiliğinden devam eder."] = "Grant access in the browser; this window continues on its own.",
        ["Tarayıcı uygulamaya geri dönmediyse, adres çubuğundaki adresi buraya yapıştırın"] = "If the browser did not return to the app, paste the address from its address bar here",
        ["Devam"] = "Continue",
    });
}
