using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ShiftRoster.Models;

namespace ShiftRoster.Services;

/// <summary>
/// Сервис базы данных SQLite.
/// Файл БД хранится прямо в папке программы.
/// При старте создается 1 копия в папке Backups, сохраняются строго 3 последние копии.
/// </summary>
public class DatabaseService
{
    private static DatabaseService? _instance;
    public static DatabaseService Instance => _instance ??= new DatabaseService();

    private readonly string _dbPath;
    private readonly string _backupsDir;
    private readonly string _connectionString;

    public string DatabasePath => _dbPath;
    public string BackupsDirectory => _backupsDir;

    public SqliteConnection CreateOpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        pragmaCmd.ExecuteNonQuery();
        return conn;
    }

    public DatabaseService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _dbPath = Path.Combine(baseDir, "ShiftRoster.db");
        _backupsDir = Path.Combine(baseDir, "Backups");
        _connectionString = $"Data Source={_dbPath}";

        PerformStartupBackup();
        InitializeDatabase();
    }

    /// <summary>
    /// Автоматическое резервное копирование при старте:
    /// Создает 1 копию и оставляет строго 3 самые свежие.
    /// </summary>
    public void PerformStartupBackup()
    {
        try
        {
            if (!File.Exists(_dbPath))
                return;

            if (!Directory.Exists(_backupsDir))
            {
                Directory.CreateDirectory(_backupsDir);
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string backupFileName = $"ShiftRoster_{timestamp}.db";
            string backupFilePath = Path.Combine(_backupsDir, backupFileName);

            File.Copy(_dbPath, backupFilePath, overwrite: true);

            // Ротация: оставляем только 3 самые свежие копии
            var backupFiles = Directory.GetFiles(_backupsDir, "ShiftRoster_*.db")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            if (backupFiles.Count > 3)
            {
                for (int i = 3; i < backupFiles.Count; i++)
                {
                    try
                    {
                        backupFiles[i].Delete();
                    }
                    catch
                    {
                        // Игнорируем ошибку удаления, если файл временно заблокирован
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка создания резервной копии: {ex.Message}");
        }
    }

    public List<FileInfo> GetBackupFiles()
    {
        if (!Directory.Exists(_backupsDir))
            return new List<FileInfo>();

        return Directory.GetFiles(_backupsDir, "ShiftRoster_*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Employees (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL,
                Position TEXT NOT NULL,
                PostName TEXT NOT NULL,
                IsPermanent INTEGER NOT NULL DEFAULT 1,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Notes TEXT,
                InitialShiftDay INTEGER NOT NULL DEFAULT 1,
                ShiftPattern TEXT NOT NULL DEFAULT '1/3',
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ShiftSchedules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                Name TEXT NOT NULL,
                PostName TEXT NOT NULL,
                PatternType TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RosterCells (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ScheduleId INTEGER NOT NULL,
                EmployeeId INTEGER,
                EmployeeName TEXT NOT NULL,
                Position TEXT NOT NULL,
                DayOfMonth INTEGER NOT NULL,
                ShiftCode TEXT NOT NULL,
                DayHours REAL NOT NULL,
                NightHours REAL NOT NULL,
                TotalHours REAL NOT NULL,
                IsSubstitute INTEGER NOT NULL DEFAULT 0,
                SubstitutedForName TEXT,
                Notes TEXT
            );

            CREATE TABLE IF NOT EXISTS Holidays (
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                Day INTEGER NOT NULL,
                IsHoliday INTEGER NOT NULL,
                IsPreHoliday INTEGER NOT NULL,
                IsWeekend INTEGER NOT NULL,
                Description TEXT,
                PRIMARY KEY (Year, Month, Day)
            );

            CREATE TABLE IF NOT EXISTS SubstituteNames (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL UNIQUE,
                Position TEXT NOT NULL DEFAULT 'Оператор (подмена)',
                DefaultReplacedEmployee TEXT,
                LastUsedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );
        ";
        command.ExecuteNonQuery();

        // Миграция существующих таблиц (если БД уже была создана ранее)
        try
        {
            using var alterCmd1 = connection.CreateCommand();
            alterCmd1.CommandText = "ALTER TABLE Employees ADD COLUMN InitialShiftDay INTEGER NOT NULL DEFAULT 1";
            alterCmd1.ExecuteNonQuery();
        }
        catch { }

        try
        {
            using var alterCmd2 = connection.CreateCommand();
            alterCmd2.CommandText = "ALTER TABLE Employees ADD COLUMN ShiftPattern TEXT NOT NULL DEFAULT '1/3'";
            alterCmd2.ExecuteNonQuery();
        }
        catch { }

        try
        {
            using var alterCmd3 = connection.CreateCommand();
            alterCmd3.CommandText = "ALTER TABLE Employees ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0";
            alterCmd3.ExecuteNonQuery();
        }
        catch { }

        try
        {
            using var alterCmd4 = connection.CreateCommand();
            alterCmd4.CommandText = "ALTER TABLE SubstituteNames ADD COLUMN Position TEXT NOT NULL DEFAULT 'Оператор (подмена)'";
            alterCmd4.ExecuteNonQuery();
        }
        catch { }

        try
        {
            using var alterCmd5 = connection.CreateCommand();
            alterCmd5.CommandText = "ALTER TABLE SubstituteNames ADD COLUMN DefaultReplacedEmployee TEXT";
            alterCmd5.ExecuteNonQuery();
        }
        catch { }
    }

    // --- Методы для работы с сотрудниками ---
    public List<Employee> GetEmployees(string? postName = null)
    {
        var result = new List<Employee>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(postName))
        {
            cmd.CommandText = "SELECT Id, FullName, Position, PostName, IsPermanent, IsActive, Notes, InitialShiftDay, ShiftPattern, SortOrder FROM Employees WHERE IsActive = 1 ORDER BY CASE WHEN SortOrder <= 0 THEN Id ELSE SortOrder END ASC, Id ASC";
        }
        else
        {
            cmd.CommandText = "SELECT Id, FullName, Position, PostName, IsPermanent, IsActive, Notes, InitialShiftDay, ShiftPattern, SortOrder FROM Employees WHERE IsActive = 1 AND PostName = @post ORDER BY CASE WHEN SortOrder <= 0 THEN Id ELSE SortOrder END ASC, Id ASC";
            cmd.Parameters.AddWithValue("@post", postName);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Employee
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Position = reader.GetString(2),
                PostName = reader.GetString(3),
                IsPermanent = reader.GetInt32(4) == 1,
                IsActive = reader.GetInt32(5) == 1,
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                InitialShiftDay = reader.IsDBNull(7) ? 1 : reader.GetInt32(7),
                ShiftPattern = reader.IsDBNull(8) ? "1/3" : reader.GetString(8),
                SortOrder = reader.IsDBNull(9) ? 0 : reader.GetInt32(9)
            });
        }
        return result;
    }

    public void SaveEmployee(Employee emp)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        if (emp.Id == 0)
        {
            cmd.CommandText = @"
                INSERT INTO Employees (FullName, Position, PostName, IsPermanent, IsActive, Notes, InitialShiftDay, ShiftPattern, SortOrder)
                VALUES (@name, @pos, @post, @perm, @act, @notes, @startDay, @pattern, @sortOrder);
                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE Employees SET FullName = @name, Position = @pos, PostName = @post, 
                IsPermanent = @perm, IsActive = @act, Notes = @notes,
                InitialShiftDay = @startDay, ShiftPattern = @pattern, SortOrder = @sortOrder WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", emp.Id);
        }

        cmd.Parameters.AddWithValue("@name", emp.FullName);
        cmd.Parameters.AddWithValue("@pos", emp.Position);
        cmd.Parameters.AddWithValue("@post", emp.PostName);
        cmd.Parameters.AddWithValue("@perm", emp.IsPermanent ? 1 : 0);
        cmd.Parameters.AddWithValue("@act", emp.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@notes", (object?)emp.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startDay", emp.InitialShiftDay);
        cmd.Parameters.AddWithValue("@pattern", emp.ShiftPattern);
        cmd.Parameters.AddWithValue("@sortOrder", emp.SortOrder);

        if (emp.Id == 0)
        {
            emp.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }
        else
        {
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateEmployeesOrder(List<Employee> employees)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        for (int i = 0; i < employees.Count; i++)
        {
            employees[i].SortOrder = i + 1;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Employees SET SortOrder = @so WHERE Id = @id";
            cmd.Parameters.AddWithValue("@so", employees[i].SortOrder);
            cmd.Parameters.AddWithValue("@id", employees[i].Id);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<string> GetAllPostNames()
    {
        var posts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT PostName FROM Employees WHERE IsActive = 1
            UNION
            SELECT DISTINCT PostName FROM ShiftSchedules";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                var post = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(post))
                {
                    posts.Add(post.Trim());
                }
            }
        }

        return posts.OrderBy(p => p).ToList();
    }

    /// <summary>
    /// Полная очистка всех смен, сотрудников и графиков (без удаления производственного календаря)
    /// </summary>
    public void ClearAllRosterData()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM RosterCells;
            DELETE FROM ShiftSchedules;
            DELETE FROM Employees;
        ";
        cmd.ExecuteNonQuery();
    }

    public string GetOrganizationName()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'OrganizationName' LIMIT 1";
        var val = cmd.ExecuteScalar() as string;
        return val ?? string.Empty;
    }

    public void SetOrganizationName(string name)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AppSettings (Key, Value) VALUES ('OrganizationName', @val)
            ON CONFLICT(Key) DO UPDATE SET Value = @val";
        cmd.Parameters.AddWithValue("@val", name.Trim());
        cmd.ExecuteNonQuery();
    }

    public string GetThemeMode()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'ThemeMode' LIMIT 1";
        var val = cmd.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(val) ? "Auto" : val;
    }

    public void SetThemeMode(string mode)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AppSettings (Key, Value) VALUES ('ThemeMode', @val)
            ON CONFLICT(Key) DO UPDATE SET Value = @val";
        cmd.Parameters.AddWithValue("@val", mode);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Получение базового месяца, к которому привязаны стартовые дни сотрудников смены
    /// </summary>
    public (int Year, int Month) GetPostBaseMonth(string postName)
    {
        if (string.IsNullOrWhiteSpace(postName)) 
            return (DateTime.Now.Year, DateTime.Now.Month);

        string key = $"PostBaseMonth_{postName.Trim().ToLowerInvariant()}";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @k LIMIT 1";
        cmd.Parameters.AddWithValue("@k", key);
        var val = cmd.ExecuteScalar() as string;
        if (!string.IsNullOrWhiteSpace(val))
        {
            var parts = val.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m))
            {
                return (y, m);
            }
        }

        // Если базовый месяц не задан явно, проверяем самый ранний сохраненный график
        using var schedCmd = conn.CreateCommand();
        schedCmd.CommandText = "SELECT Year, Month FROM ShiftSchedules WHERE PostName = @p ORDER BY Year ASC, Month ASC LIMIT 1";
        schedCmd.Parameters.AddWithValue("@p", postName);
        using var reader = schedCmd.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetInt32(0), reader.GetInt32(1));
        }

        return (DateTime.Now.Year, DateTime.Now.Month);
    }

    /// <summary>
    /// Задание базового месяца привязки дней заступления смены
    /// </summary>
    public void SetPostBaseMonth(string postName, int year, int month)
    {
        if (string.IsNullOrWhiteSpace(postName)) return;

        string key = $"PostBaseMonth_{postName.Trim().ToLowerInvariant()}";
        string val = $"{year:D4}-{month:D2}";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AppSettings (Key, Value) VALUES (@k, @v)
            ON CONFLICT(Key) DO UPDATE SET Value = @v";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", val);
        cmd.ExecuteNonQuery();
    }

    public List<ShiftSchedule> GetAllSchedulesForMonth(int year, int month)
    {
        var result = new List<ShiftSchedule>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Year, Month, Name, PostName, PatternType, CreatedAt FROM ShiftSchedules WHERE Year = @y AND Month = @m ORDER BY PostName";
        cmd.Parameters.AddWithValue("@y", year);
        cmd.Parameters.AddWithValue("@m", month);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ShiftSchedule
            {
                Id = reader.GetInt32(0),
                Year = reader.GetInt32(1),
                Month = reader.GetInt32(2),
                Name = reader.GetString(3),
                PostName = reader.GetString(4),
                PatternType = reader.GetString(5),
                CreatedAt = DateTime.Parse(reader.GetString(6))
            });
        }

        return result;
    }

    public void DeleteEmployee(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        string empName = string.Empty;
        using (var nameCmd = conn.CreateCommand())
        {
            nameCmd.Transaction = tx;
            nameCmd.CommandText = "SELECT FullName FROM Employees WHERE Id = @id";
            nameCmd.Parameters.AddWithValue("@id", id);
            empName = nameCmd.ExecuteScalar() as string ?? string.Empty;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Employees SET IsActive = 0 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        if (!string.IsNullOrWhiteSpace(empName))
        {
            using var cmdCells = conn.CreateCommand();
            cmdCells.Transaction = tx;
            cmdCells.CommandText = "DELETE FROM RosterCells WHERE EmployeeName = @name";
            cmdCells.Parameters.AddWithValue("@name", empName);
            cmdCells.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>
    /// Полное удаление смены/поста, ее сотрудников, графиков и ячеек табеля
    /// </summary>
    public void DeletePost(string postName)
    {
        if (string.IsNullOrWhiteSpace(postName)) return;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd1 = conn.CreateCommand();
        cmd1.Transaction = tx;
        cmd1.CommandText = @"
            DELETE FROM RosterCells 
            WHERE ScheduleId IN (SELECT Id FROM ShiftSchedules WHERE PostName = @p)";
        cmd1.Parameters.AddWithValue("@p", postName);
        cmd1.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.Transaction = tx;
        cmd2.CommandText = "DELETE FROM ShiftSchedules WHERE PostName = @p";
        cmd2.Parameters.AddWithValue("@p", postName);
        cmd2.ExecuteNonQuery();

        using var cmd3 = conn.CreateCommand();
        cmd3.Transaction = tx;
        cmd3.CommandText = "DELETE FROM Employees WHERE PostName = @p";
        cmd3.Parameters.AddWithValue("@p", postName);
        cmd3.ExecuteNonQuery();

        tx.Commit();
    }

    public string GetPostPattern(string postName)
    {
        if (string.IsNullOrWhiteSpace(postName)) return "1/3";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PatternType FROM ShiftSchedules WHERE PostName = @p ORDER BY Id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@p", postName);
        var val = cmd.ExecuteScalar() as string;
        if (!string.IsNullOrWhiteSpace(val)) return val;

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT ShiftPattern FROM Employees WHERE PostName = @p AND IsActive = 1 LIMIT 1";
        cmd2.Parameters.AddWithValue("@p", postName);
        var val2 = cmd2.ExecuteScalar() as string;
        return !string.IsNullOrWhiteSpace(val2) ? val2 : "1/3";
    }

    public void SetPostPattern(string postName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(postName)) return;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ShiftSchedules SET PatternType = @pat WHERE PostName = @p";
        cmd.Parameters.AddWithValue("@pat", pattern);
        cmd.Parameters.AddWithValue("@p", postName);
        cmd.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "UPDATE Employees SET ShiftPattern = @pat WHERE PostName = @p";
        cmd2.Parameters.AddWithValue("@pat", pattern);
        cmd2.Parameters.AddWithValue("@p", postName);
        cmd2.ExecuteNonQuery();
    }

    // --- Методы для работы с графиками и ячейками табеля ---
    public ShiftSchedule GetOrCreateSchedule(int year, int month, string postName, string defaultPattern = "1/3")
    {
        if (string.IsNullOrWhiteSpace(postName))
        {
            return new ShiftSchedule
            {
                Year = year,
                Month = month,
                Name = "Подразделение не выбрано",
                PostName = string.Empty,
                PatternType = defaultPattern
            };
        }

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Year, Month, Name, PostName, PatternType, CreatedAt FROM ShiftSchedules WHERE Year = @y AND Month = @m AND PostName = @p LIMIT 1";
        cmd.Parameters.AddWithValue("@y", year);
        cmd.Parameters.AddWithValue("@m", month);
        cmd.Parameters.AddWithValue("@p", postName);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new ShiftSchedule
            {
                Id = reader.GetInt32(0),
                Year = reader.GetInt32(1),
                Month = reader.GetInt32(2),
                Name = reader.GetString(3),
                PostName = reader.GetString(4),
                PatternType = reader.GetString(5),
                CreatedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        reader.Close();

        // Создаем новый график
        var schedule = new ShiftSchedule
        {
            Year = year,
            Month = month,
            Name = $"{postName} ({ShiftSchedule.GetMonthName(month)} {year})",
            PostName = postName,
            PatternType = defaultPattern,
            CreatedAt = DateTime.Now
        };

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO ShiftSchedules (Year, Month, Name, PostName, PatternType, CreatedAt)
            VALUES (@y, @m, @n, @p, @pat, @c);
            SELECT last_insert_rowid();";
        insertCmd.Parameters.AddWithValue("@y", schedule.Year);
        insertCmd.Parameters.AddWithValue("@m", schedule.Month);
        insertCmd.Parameters.AddWithValue("@n", schedule.Name);
        insertCmd.Parameters.AddWithValue("@p", schedule.PostName);
        insertCmd.Parameters.AddWithValue("@pat", schedule.PatternType);
        insertCmd.Parameters.AddWithValue("@c", schedule.CreatedAt.ToString("o"));

        schedule.Id = Convert.ToInt32(insertCmd.ExecuteScalar());
        return schedule;
    }

    public List<RosterCell> GetRosterCells(int scheduleId)
    {
        var result = new List<RosterCell>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, ScheduleId, EmployeeId, EmployeeName, Position, DayOfMonth, 
                   ShiftCode, DayHours, NightHours, TotalHours, IsSubstitute, SubstitutedForName, Notes
            FROM RosterCells
            WHERE ScheduleId = @sid
            ORDER BY IsSubstitute ASC, Id ASC, DayOfMonth ASC";
        cmd.Parameters.AddWithValue("@sid", scheduleId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new RosterCell
            {
                Id = reader.GetInt32(0),
                ScheduleId = reader.GetInt32(1),
                EmployeeId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                EmployeeName = reader.GetString(3),
                Position = reader.GetString(4),
                DayOfMonth = reader.GetInt32(5),
                ShiftCode = reader.GetString(6),
                DayHours = reader.GetDouble(7),
                NightHours = reader.GetDouble(8),
                TotalHours = reader.GetDouble(9),
                IsSubstitute = reader.GetInt32(10) == 1,
                SubstitutedForName = reader.IsDBNull(11) ? null : reader.GetString(11),
                Notes = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }
        return result;
    }

    public void SaveRosterCells(int scheduleId, IEnumerable<RosterCell> cells)
    {
        using var conn = CreateOpenConnection();
        using var transaction = conn.BeginTransaction();

        // Удаляем существующие ячейки для этого графика и записываем актуальные
        using var delCmd = conn.CreateCommand();
        delCmd.Transaction = transaction;
        delCmd.CommandText = "DELETE FROM RosterCells WHERE ScheduleId = @sid";
        delCmd.Parameters.AddWithValue("@sid", scheduleId);
        delCmd.ExecuteNonQuery();

        using var insCmd = conn.CreateCommand();
        insCmd.Transaction = transaction;
        insCmd.CommandText = @"
            INSERT INTO RosterCells (
                ScheduleId, EmployeeId, EmployeeName, Position, DayOfMonth,
                ShiftCode, DayHours, NightHours, TotalHours, IsSubstitute, SubstitutedForName, Notes
            ) VALUES (
                @sid, @eid, @ename, @pos, @day,
                @code, @dh, @nh, @th, @sub, @subfor, @notes
            )";

        var pSid = insCmd.Parameters.Add("@sid", SqliteType.Integer);
        var pEid = insCmd.Parameters.Add("@eid", SqliteType.Integer);
        var pEname = insCmd.Parameters.Add("@ename", SqliteType.Text);
        var pPos = insCmd.Parameters.Add("@pos", SqliteType.Text);
        var pDay = insCmd.Parameters.Add("@day", SqliteType.Integer);
        var pCode = insCmd.Parameters.Add("@code", SqliteType.Text);
        var pDh = insCmd.Parameters.Add("@dh", SqliteType.Real);
        var pNh = insCmd.Parameters.Add("@nh", SqliteType.Real);
        var pTh = insCmd.Parameters.Add("@th", SqliteType.Real);
        var pSub = insCmd.Parameters.Add("@sub", SqliteType.Integer);
        var pSubFor = insCmd.Parameters.Add("@subfor", SqliteType.Text);
        var pNotes = insCmd.Parameters.Add("@notes", SqliteType.Text);

        pSid.Value = scheduleId;

        foreach (var cell in cells)
        {
            pEid.Value = (object?)cell.EmployeeId ?? DBNull.Value;
            pEname.Value = cell.EmployeeName;
            pPos.Value = cell.Position;
            pDay.Value = cell.DayOfMonth;
            pCode.Value = cell.ShiftCode;
            pDh.Value = cell.DayHours;
            pNh.Value = cell.NightHours;
            pTh.Value = cell.TotalHours;
            pSub.Value = cell.IsSubstitute ? 1 : 0;
            pSubFor.Value = (object?)cell.SubstitutedForName ?? DBNull.Value;
            pNotes.Value = (object?)cell.Notes ?? DBNull.Value;

            insCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // --- Список ранее использованных подменных работников ---
    public List<SubstituteRecord> GetSubstitutes()
    {
        var result = new List<SubstituteRecord>();
        using var conn = CreateOpenConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, FullName, Position, DefaultReplacedEmployee, LastUsedAt FROM SubstituteNames ORDER BY LastUsedAt DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SubstituteRecord
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Position = reader.IsDBNull(2) ? "Оператор (подмена)" : reader.GetString(2),
                DefaultReplacedEmployee = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastUsedAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.Now
            });
        }
        return result;
    }

    public SubstituteRecord? GetSubstituteForEmployee(string employeeName)
    {
        if (string.IsNullOrWhiteSpace(employeeName)) return null;

        using var conn = CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, FullName, Position, DefaultReplacedEmployee, LastUsedAt FROM SubstituteNames WHERE DefaultReplacedEmployee = @emp ORDER BY LastUsedAt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@emp", employeeName.Trim());

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new SubstituteRecord
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Position = reader.IsDBNull(2) ? "Оператор (подмена)" : reader.GetString(2),
                DefaultReplacedEmployee = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastUsedAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.Now
            };
        }
        return null;
    }

    public void SaveSubstitute(string fullName, string position, string? replacedEmployee = null)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return;

        string cleanName = fullName.Trim();
        string cleanPos = string.IsNullOrWhiteSpace(position) ? "Оператор (подмена)" : position.Trim();
        string? cleanRep = string.IsNullOrWhiteSpace(replacedEmployee) ? null : replacedEmployee.Trim();

        using var conn = CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SubstituteNames (FullName, Position, DefaultReplacedEmployee, LastUsedAt)
            VALUES (@name, @pos, @rep, @now)
            ON CONFLICT(FullName) DO UPDATE SET 
                Position = @pos,
                DefaultReplacedEmployee = COALESCE(@rep, DefaultReplacedEmployee),
                LastUsedAt = @now";
        cmd.Parameters.AddWithValue("@name", cleanName);
        cmd.Parameters.AddWithValue("@pos", cleanPos);
        cmd.Parameters.AddWithValue("@rep", (object?)cleanRep ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<string> GetRecentSubstituteNames()
    {
        return GetSubstitutes().Select(s => s.FullName).ToList();
    }

    public void RecordSubstituteName(string fullName)
    {
        SaveSubstitute(fullName, "Оператор (подмена)");
    }

    // --- Праздничные дни календаря ---
    public List<RussianCalendarDay> GetHolidays(int year)
    {
        var result = new List<RussianCalendarDay>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Year, Month, Day, IsHoliday, IsPreHoliday, IsWeekend, Description FROM Holidays WHERE Year = @y";
        cmd.Parameters.AddWithValue("@y", year);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new RussianCalendarDay
            {
                Year = reader.GetInt32(0),
                Month = reader.GetInt32(1),
                Day = reader.GetInt32(2),
                IsHoliday = reader.GetInt32(3) == 1,
                IsPreHoliday = reader.GetInt32(4) == 1,
                IsWeekend = reader.GetInt32(5) == 1,
                Description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            });
        }
        return result;
    }

    public void SaveHolidays(int year, IEnumerable<RussianCalendarDay> days)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var trans = conn.BeginTransaction();

        using var del = conn.CreateCommand();
        del.Transaction = trans;
        del.CommandText = "DELETE FROM Holidays WHERE Year = @y";
        del.Parameters.AddWithValue("@y", year);
        del.ExecuteNonQuery();

        foreach (var d in days)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = trans;
            ins.CommandText = @"
                INSERT OR REPLACE INTO Holidays (Year, Month, Day, IsHoliday, IsPreHoliday, IsWeekend, Description)
                VALUES (@y, @m, @d, @h, @ph, @w, @desc)";
            ins.Parameters.AddWithValue("@y", d.Year);
            ins.Parameters.AddWithValue("@m", d.Month);
            ins.Parameters.AddWithValue("@d", d.Day);
            ins.Parameters.AddWithValue("@h", d.IsHoliday ? 1 : 0);
            ins.Parameters.AddWithValue("@ph", d.IsPreHoliday ? 1 : 0);
            ins.Parameters.AddWithValue("@w", d.IsWeekend ? 1 : 0);
            ins.Parameters.AddWithValue("@desc", d.Description);
            ins.ExecuteNonQuery();
        }

        trans.Commit();
    }
}
