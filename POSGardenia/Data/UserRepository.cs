using Microsoft.Data.Sqlite;
using POSGardenia.Models;
using System;
using System.Collections.Generic;

namespace POSGardenia.Data
{
    public class UserRepository
    {
        private const string Columns =
            "Id, Username, PasswordHash, FullName, NicNumber, Phone, Address, DateOfBirth, Email, EmergencyContact, " +
            "JoinedDate, IsAdmin, IsActive, Permissions, CreatedAt, LastLoginAt";

        public int Count()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Users;";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public int CountActiveAdmins()
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Users WHERE IsAdmin = 1 AND IsActive = 1;";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public List<AppUser> GetAll()
        {
            var users = new List<AppUser>();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM Users ORDER BY IsActive DESC, FullName;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
                users.Add(Map(reader));

            return users;
        }

        public AppUser? GetById(int id)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM Users WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        // Usernames are not case-sensitive.
        public AppUser? GetByUsername(string username)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM Users WHERE Username = @username COLLATE NOCASE;";
            command.Parameters.AddWithValue("@username", username);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        public int Add(AppUser user)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Users
                    (Username, PasswordHash, FullName, NicNumber, Phone, Address, DateOfBirth, Email, EmergencyContact,
                     JoinedDate, IsAdmin, IsActive, Permissions, CreatedAt)
                VALUES
                    (@username, @hash, @fullName, @nic, @phone, @address, @dob, @email, @emergency,
                     @joined, @isAdmin, 1, @permissions, @createdAt);
                SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@username", user.Username);
            command.Parameters.AddWithValue("@hash", user.PasswordHash);
            AddProfile(command, user);
            command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return Convert.ToInt32(command.ExecuteScalar());
        }

        // Personal details and access. The username and password are not touched here.
        public void UpdateProfile(AppUser user)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Users
                SET FullName = @fullName, NicNumber = @nic, Phone = @phone, Address = @address, DateOfBirth = @dob,
                    Email = @email, EmergencyContact = @emergency, JoinedDate = @joined,
                    IsAdmin = @isAdmin, Permissions = @permissions
                WHERE Id = @id;";

            command.Parameters.AddWithValue("@id", user.Id);
            AddProfile(command, user);
            command.ExecuteNonQuery();
        }

        public void SetPasswordHash(int id, string hash)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE Id = @id;";
            command.Parameters.AddWithValue("@hash", hash);
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        public void SetActive(int id, bool active)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET IsActive = @active WHERE Id = @id;";
            command.Parameters.AddWithValue("@active", active ? 1 : 0);
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        public void TouchLastLogin(int id)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET LastLoginAt = @now WHERE Id = @id;";
            command.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        private static void AddProfile(SqliteCommand command, AppUser user)
        {
            command.Parameters.AddWithValue("@fullName", user.FullName);
            command.Parameters.AddWithValue("@nic", user.NicNumber);
            command.Parameters.AddWithValue("@phone", user.Phone);
            command.Parameters.AddWithValue("@address", user.Address);
            command.Parameters.AddWithValue("@dob", string.IsNullOrWhiteSpace(user.DateOfBirth) ? DBNull.Value : user.DateOfBirth);
            command.Parameters.AddWithValue("@email", user.Email);
            command.Parameters.AddWithValue("@emergency", user.EmergencyContact);
            command.Parameters.AddWithValue("@joined", user.JoinedDate);
            command.Parameters.AddWithValue("@isAdmin", user.IsAdmin ? 1 : 0);
            command.Parameters.AddWithValue("@permissions", AppPermissions.Join(user.Permissions));
        }

        private static AppUser Map(SqliteDataReader reader) => new()
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            FullName = reader.GetString(3),
            NicNumber = reader.GetString(4),
            Phone = reader.GetString(5),
            Address = reader.GetString(6),
            DateOfBirth = reader.IsDBNull(7) ? null : reader.GetString(7),
            Email = reader.GetString(8),
            EmergencyContact = reader.GetString(9),
            JoinedDate = reader.GetString(10),
            IsAdmin = reader.GetInt32(11) == 1,
            IsActive = reader.GetInt32(12) == 1,
            Permissions = AppPermissions.Parse(reader.GetString(13)),
            CreatedAt = reader.GetString(14),
            LastLoginAt = reader.IsDBNull(15) ? null : reader.GetString(15)
        };
    }
}
