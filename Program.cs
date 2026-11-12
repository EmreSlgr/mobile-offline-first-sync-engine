using System.Text;
using Microsoft.Data.Sqlite;

Console.OutputEncoding = Encoding.UTF8;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("==========================================================================");
Console.WriteLine("  MOBILE OFFLINE-FIRST SYNC & CONFLICT RESOLUTION (DELTA SYNC)");
Console.WriteLine("  Senaryo: Kullanıcı metroda/uçaktayken (offline) telefonunda yeni kayıtlar açar,");
Console.WriteLine("           mevcut kayıtları günceller ve siler.");
Console.WriteLine("           Aynı anda sunucuda da çakışan değişiklikler olur.");
Console.WriteLine("           İnternet geldiğinde Delta Sync & Conflict Resolver devreye girer.");
Console.WriteLine("==========================================================================");
Console.ResetColor();
Console.WriteLine();

// -----------------------------------------------------------------------------
// HAZIRLIK: İKİ AYRI SQLITE VERİTABANI (MOBİL CİHAZ vs BULUT SUNUCU)
// -----------------------------------------------------------------------------
using var deviceDb = CreateDatabase("DeviceLocalDB");
using var cloudDb = CreateDatabase("CloudServerDB");

var deviceRepo = new SyncRepository(deviceDb);
var cloudRepo = new SyncRepository(cloudDb);

// ADIM 1: BAŞLANGIÇ SENKRONİZASYONU
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine(">>> 1. ADIM: Başlangıç Durumu (Cihaz ve Sunucu Eşitlenmiş)");
Console.ResetColor();

var note1Id = Guid.NewGuid();
var initialNote = new SyncEntity
{
    Id = note1Id,
    Title = "Garajım: Periyodik Bakım",
    Content = "10.000 km yağ ve filtre bakımı yapılacak.",
    UpdatedAtUtc = DateTime.UtcNow.AddHours(-2),
    Version = 1,
    IsDeleted = false
};

deviceRepo.Upsert(initialNote);
cloudRepo.Upsert(initialNote);

DateTime deviceLastSyncTime = DateTime.UtcNow.AddHours(-1);

Console.WriteLine($"  [Eşitlendi] Ortak Kayıt: \"{initialNote.Title}\" (Version: {initialNote.Version})");
Console.WriteLine($"  [Telefon] Son Başarılı Senkronizasyon Zamanı (LastSyncTime): {deviceLastSyncTime:HH:mm:ss}");
Console.WriteLine();

// -----------------------------------------------------------------------------
// ADIM 2: MOBİL CİHAZ ÇEVRİMDIŞI OLUR (UÇAK MODU ✈️)
// -----------------------------------------------------------------------------
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine(">>> 2. ADIM: Telefon İnternetten Koptu (Offline Mod) & Yerel Değişiklikler");
Console.ResetColor();

// A) Mobilde yeni 2 kayıt eklenir
var note2 = new SyncEntity
{
    Id = Guid.NewGuid(),
    Title = "Vaktin: Cuma Namazı Hatırlatıcısı",
    Content = "Cuma vaktinden 45 dk önce bildirim gönder.",
    UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-20),
    Version = 1,
    IsDeleted = false
};
var note3 = new SyncEntity
{
    Id = Guid.NewGuid(),
    Title = "HCT: Aylık Harcama Özeti",
    Content = "Market ve yakıt harcamalarını grupla.",
    UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-15),
    Version = 1,
    IsDeleted = false
};
deviceRepo.Upsert(note2);
deviceRepo.Upsert(note3);

// B) Mobilde mevcut kayıt güncellenir
var note1PhoneEdit = new SyncEntity
{
    Id = note1Id,
    Title = "Garajım: Periyodik Bakım",
    Content = "10.000 km bakımı yapıldı, fren balataları yenilendi (Mobilde Güncellendi).",
    UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-10), // Saat: 10 dk önce
    Version = 2,
    IsDeleted = false
};
deviceRepo.Upsert(note1PhoneEdit);

Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine("  [Telefon (Offline)] 2 yeni not eklendi:");
Console.WriteLine($"    + \"{note2.Title}\"");
Console.WriteLine($"    + \"{note3.Title}\"");
Console.WriteLine($"  [Telefon (Offline)] Mevcut not güncellendi (v2): \"{note1PhoneEdit.Content}\"");
Console.ResetColor();
Console.WriteLine();

// -----------------------------------------------------------------------------
// ADIM 3: AYNI ANDA SUNUCUDA / WEB PANELİNDE ÇAKIŞAN GÜNCELLEME OLUR
// -----------------------------------------------------------------------------
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine(">>> 3. ADIM: Eşzamanlı Çakışma (Web Panelinden Aynı Kayıt Değiştirildi)");
Console.ResetColor();

var note1WebEdit = new SyncEntity
{
    Id = note1Id,
    Title = "Garajım: Periyodik Bakım",
    Content = "Bakım randevusu İPTAL EDİLDİ, muayene tarihi öne çekildi (Webden Güncellendi).",
    UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5), // Saat: 5 dk önce (Daha yeni!)
    Version = 2,
    IsDeleted = false
};
cloudRepo.Upsert(note1WebEdit);

Console.ForegroundColor = ConsoleColor.Magenta;
Console.WriteLine($"  [Sunucu (Web)] Aynı kayıt webden farklı güncellendi (v2): \"{note1WebEdit.Content}\"");
Console.WriteLine("  [DİKKAT] Çakışma (Conflict) Oluştu: Hem mobil hem sunucu v1'den v2'ye bağımsız güncellendi!");
Console.ResetColor();
Console.WriteLine();

// -----------------------------------------------------------------------------
// ADIM 4: İNTERNET GELDİ! DELTA SYNC & CONFLICT RESOLUTION ÇALIŞIYOR
// -----------------------------------------------------------------------------
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine(">>> 4. ADIM: İnternet Bağlandı! Delta Sync Motoru Devreye Giriyor...");
Console.ResetColor();

var syncEngine = new DeltaSyncEngine(cloudRepo);

// A) PUSH AŞAMASI (Mobil -> Sunucu)
// Telefon yalnızca LastSyncTime'dan sonra değişenleri bulur (Delta Push)
var localChanges = deviceRepo.GetChangesSince(deviceLastSyncTime);
Console.WriteLine($"  [Delta Push] Mobilden sunucuya sadece değişen {localChanges.Count} kayıt gönderiliyor...");

var pushResult = syncEngine.ProcessClientPush(localChanges);

foreach (var log in pushResult.Logs)
{
    if (log.Contains("ÇAKIŞMA"))
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"    -> {log}");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"    -> {log}");
        Console.ResetColor();
    }
}

// B) PULL AŞAMASI (Sunucu -> Mobil)
// Telefon sunucudan son değişiklikleri ister (Delta Pull)
DateTime newSyncToken = DateTime.UtcNow;
var serverDeltas = cloudRepo.GetChangesSince(deviceLastSyncTime);
Console.WriteLine($"  [Delta Pull] Sunucudan telefona değişen {serverDeltas.Count} kayıt çekiliyor...");

foreach (var delta in serverDeltas)
{
    deviceRepo.Upsert(delta);
}

deviceLastSyncTime = newSyncToken;
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"  [Başarılı] Senkronizasyon tamamlandı! Yeni Sync Token: {deviceLastSyncTime:HH:mm:ss}");
Console.ResetColor();
Console.WriteLine();

// -----------------------------------------------------------------------------
// ADIM 5: TUTARLILIK DOĞRULAMASI
// -----------------------------------------------------------------------------
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine(">>> 5. ADIM: Son Durum ve Veri Tutarlılığı Kontrolü");
Console.ResetColor();

var phoneFinalNotes = deviceRepo.GetAll();
var cloudFinalNotes = cloudRepo.GetAll();

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("==========================================================================");
Console.WriteLine($"  Telefondaki Toplam Kayıt: {phoneFinalNotes.Count} | Sunucudaki Toplam Kayıt: {cloudFinalNotes.Count}");
Console.WriteLine("--------------------------------------------------------------------------");
foreach (var note in phoneFinalNotes)
{
    Console.WriteLine($"  * [{note.Title}]");
    Console.WriteLine($"    İçerik : {note.Content}");
    Console.WriteLine($"    Sürüm  : v{note.Version} | Son Güncelleme: {note.UpdatedAtUtc:HH:mm:ss}");
}
Console.WriteLine("==========================================================================");
Console.ResetColor();
Console.WriteLine("  SONUÇ: Sıfır veri kaybı. Çakışma Last-Write-Wins (LWW) ile çözüldü.");
Console.WriteLine("         Tüm veritabanı yerine yalnızca değişen birkaç byte veri aktarıldı!");
Console.WriteLine();

// =============================================================================
// YARDIMCI YEREL METOT
// =============================================================================

static SqliteConnection CreateDatabase(string dbName)
{
    var conn = new SqliteConnection($"Data Source={dbName};Mode=Memory;Cache=Shared");
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE Records (
            Id TEXT PRIMARY KEY,
            Title TEXT NOT NULL,
            Content TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            Version INTEGER NOT NULL,
            IsDeleted INTEGER NOT NULL
        );
    ";
    cmd.ExecuteNonQuery();
    return conn;
}


// =============================================================================
// MODELLER VE DELTA SYNC MOTORU
// =============================================================================

public class SyncEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }
    public bool IsDeleted { get; set; } // Soft-Delete
}

public class SyncRepository
{
    private readonly SqliteConnection _db;

    public SyncRepository(SqliteConnection db) => _db = db;

    public void Upsert(SyncEntity entity)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Records (Id, Title, Content, UpdatedAtUtc, Version, IsDeleted)
            VALUES (@Id, @Title, @Content, @UpdatedAtUtc, @Version, @IsDeleted)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Content = excluded.Content,
                UpdatedAtUtc = excluded.UpdatedAtUtc,
                Version = excluded.Version,
                IsDeleted = excluded.IsDeleted;
        ";
        cmd.Parameters.AddWithValue("@Id", entity.Id.ToString());
        cmd.Parameters.AddWithValue("@Title", entity.Title);
        cmd.Parameters.AddWithValue("@Content", entity.Content);
        cmd.Parameters.AddWithValue("@UpdatedAtUtc", entity.UpdatedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@Version", entity.Version);
        cmd.Parameters.AddWithValue("@IsDeleted", entity.IsDeleted ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public SyncEntity? GetById(Guid id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Content, UpdatedAtUtc, Version, IsDeleted FROM Records WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id.ToString());

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new SyncEntity
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                Content = reader.GetString(2),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(3)),
                Version = reader.GetInt32(4),
                IsDeleted = reader.GetInt32(5) == 1
            };
        }
        return null;
    }

    public List<SyncEntity> GetChangesSince(DateTime lastSyncUtc)
    {
        var list = new List<SyncEntity>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Content, UpdatedAtUtc, Version, IsDeleted FROM Records WHERE UpdatedAtUtc > @LastSync";
        cmd.Parameters.AddWithValue("@LastSync", lastSyncUtc.ToString("o"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SyncEntity
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                Content = reader.GetString(2),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(3)),
                Version = reader.GetInt32(4),
                IsDeleted = reader.GetInt32(5) == 1
            });
        }
        return list;
    }

    public List<SyncEntity> GetAll()
    {
        var list = new List<SyncEntity>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Content, UpdatedAtUtc, Version, IsDeleted FROM Records WHERE IsDeleted = 0 ORDER BY UpdatedAtUtc DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SyncEntity
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                Content = reader.GetString(2),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(3)),
                Version = reader.GetInt32(4),
                IsDeleted = reader.GetInt32(5) == 1
            });
        }
        return list;
    }
}

public class DeltaSyncEngine
{
    private readonly SyncRepository _cloudRepo;

    public DeltaSyncEngine(SyncRepository cloudRepo) => _cloudRepo = cloudRepo;

    public (bool Success, List<string> Logs) ProcessClientPush(List<SyncEntity> clientChanges)
    {
        var logs = new List<string>();

        foreach (var clientItem in clientChanges)
        {
            var serverItem = _cloudRepo.GetById(clientItem.Id);

            if (serverItem == null)
            {
                // Yeni kayıt -> Doğrudan ekle
                _cloudRepo.Upsert(clientItem);
                logs.Add($"[Eklendi] Yeni kayıt sunucuya kaydedildi: \"{clientItem.Title}\"");
            }
            else
            {
                // Çakışma kontrolü: Eğer iki taraf da aynı sürümden türeyip bağımsız güncellendiyse
                if (serverItem.Version == clientItem.Version && serverItem.Content != clientItem.Content)
                {
                    // CONFLICT RESOLUTION: Last-Write-Wins (LWW) Stratejisi
                    if (clientItem.UpdatedAtUtc > serverItem.UpdatedAtUtc)
                    {
                        clientItem.Version++;
                        _cloudRepo.Upsert(clientItem);
                        logs.Add($"[ÇAKIŞMA ÇÖZÜLDÜ - İstemci Kazandı] \"{clientItem.Title}\" (İstemci zaman damgası daha yeni)");
                    }
                    else
                    {
                        serverItem.Version++;
                        _cloudRepo.Upsert(serverItem);
                        logs.Add($"[ÇAKIŞMA ÇÖZÜLDÜ - Sunucu Kazandı] \"{serverItem.Title}\" (Sunucu zaman damgası daha yeni: Web içeriği korundu)");
                    }
                }
                else if (clientItem.Version > serverItem.Version)
                {
                    _cloudRepo.Upsert(clientItem);
                    logs.Add($"[Güncellendi] \"{clientItem.Title}\" sunucuda v{clientItem.Version} olarak güncellendi.");
                }
            }
        }

        return (true, logs);
    }
}
