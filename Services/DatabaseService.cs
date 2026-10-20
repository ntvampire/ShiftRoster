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
                ShiftPattern TEXT NOT NULL DEFAULT '1/3'
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
                LastUsedAt TEXT NOT NULL
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

        // Проверяем, есть ли сотрудники. Если нет — создаем начальный штат для демонстрации
        command.CommandText = "SELECT COUNT(*) FROM Employees";
        long empCount = (long)(command.ExecuteScalar() ?? 0);
        if (empCount == 0)
        {
            SeedInitialEmployees(connection);
        }
    }

    private void SeedInitialEmployees(SqliteConnection connection)
    {
        var defaults = new[]
        {
            ("Иванов Сергей Петрович", "Старший смены", "Смена №1 (Пост 1)", 1, "1/3"),
            ("Петров Алексей Владимирович", "Дежурный оператор", "Смена №1 (Пост 1)", 2, "1/3"),
            ("Сидоров Михаил Николаевич", "Дежурный оператор", "Смена №1 (Пост 1)", 3, "1/3"),
            ("Кузнецов Дмитрий Александрович", "Дежурный оператор", "Смена №1 (Пост 1)", 4, "1/3"),
            ("Васильев Роман Игоревич", "Старший смены", "Смена №2 (Пост 2)", 1, "1/3"),
            ("Николаев Виктор Сергеевич", "Дежурный оператор", "Смена №2 (Пост 2)", 2, "1/3"),
            ("Федоров Андрей Павлович", "Дежурный оператор", "Смена №2 (Пост 2)", 3, "1/3"),
            ("Морозов Константин Юрьевич", "Дежурный оператор", "Смена №2 (Пост 2)", 4, "1/3")
        };

        foreach (var (name, pos, post, startDay, pattern) in defaults)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Employees (FullName, Position, PostName, IsPermanent, IsActive, InitialShiftDay, ShiftPattern)
                VALUES (@name, @pos, @post, 1, 1, @startDay, @pattern)";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@pos", pos);
            cmd.Parameters.AddWithValue("@post", post);
            cmd.Parameters.AddWithValue("@startDay", startDay);
            cmd.Parameters.AddWithValue("@pattern", pattern);
            cmd.ExecuteNonQuery();
        }
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
            cmd.CommandText = "SELECT Id, FullName, Position, PostName, IsPermanent, IsActive, Notes, InitialShiftDay, ShiftPattern FROM Employees WHERE IsActive = 1 ORDER BY FullName";
        }
        else
        {
            cmd.CommandText = "SELECT Id, FullName, Position, PostName, IsPermanent, IsActive, Notes, InitialShiftDay, ShiftPattern FROM Employees WHERE IsActive = 1 AND PostName = @post ORDER BY InitialShiftDay, FullName";
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
                ShiftPattern = reader.IsDBNull(8) ? "1/3" : reader.GetString(8)
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
                INSERT INTO Employees (FullName, Position, PostName, IsPermanent, IsActive, Notes, InitialShiftDay, ShiftPattern)
                VALUES (@name, @pos, @post, @perm, @act, @notes, @startDay, @pattern);
                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE Employees SET FullName = @name, Position = @pos, PostName = @post, 
                IsPermanent = @perm, IsActive = @act, Notes = @notes,
                InitialShiftDay = @startDay, ShiftPattern = @pattern WHERE Id = @id";
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

        if (emp.Id == 0)
        {
            emp.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }
        else
        {
            cmd.ExecuteNonQuery();
        }
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

        if (posts.Count == 0)
        {
            posts.Add("Смена №1 (Пост 1)");
            posts.Add("Смена №2 (Пост 2)");
        }

        return posts.OrderBy(p => p).ToList();
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Employees SET IsActive = 0 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // --- Методы для работы с графиками и ячейками табеля ---
    public ShiftSchedule GetOrCreateSchedule(int year, int month, string postName, string defaultPattern = "1/3")
    {
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
            ORDER BY EmployeeName, DayOfMonth";
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
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();

        // Удаляем существующие ячейки для этого графика и записываем актуальные
        using var delCmd = conn.CreateCommand();
        delCmd.Transaction = transaction;
        delCmd.CommandText = "DELETE FROM RosterCells WHERE ScheduleId = @sid";
        delCmd.Parameters.AddWithValue("@sid", scheduleId);
        delCmd.ExecuteNonQuery();

        foreach (var cell in cells)
        {
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
            insCmd.Parameters.AddWithValue("@sid", scheduleId);
            insCmd.Parameters.AddWithValue("@eid", (object?)cell.EmployeeId ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("@ename", cell.EmployeeName);
            insCmd.Parameters.AddWithValue("@pos", cell.Position);
            insCmd.Parameters.AddWithValue("@day", cell.DayOfMonth);
            insCmd.Parameters.AddWithValue("@code", cell.ShiftCode);
            insCmd.Parameters.AddWithValue("@dh", cell.DayHours);
            insCmd.Parameters.AddWithValue("@nh", cell.NightHours);
            insCmd.Parameters.AddWithValue("@th", cell.TotalHours);
            insCmd.Parameters.AddWithValue("@sub", cell.IsSubstitute ? 1 : 0);
            insCmd.Parameters.AddWithValue("@subfor", (object?)cell.SubstitutedForName ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("@notes", (object?)cell.Notes ?? DBNull.Value);

            insCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // --- Список ранее использованных подменных работников ---
    public List<string> GetRecentSubstituteNames()
    {
        var result = new List<string>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FullName FROM SubstituteNames ORDER BY LastUsedAt DESC LIMIT 20";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    public void RecordSubstituteName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SubstituteNames (FullName, LastUsedAt)
            VALUES (@name, @now)
            ON CONFLICT(FullName) DO UPDATE SET LastUsedAt = @now";
        cmd.Parameters.AddWithValue("@name", fullName.Trim());
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
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
