using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace StudentBadge.Services
{
    public class PasswordHashService
    {
        // Generate a hashed password with a random salt
        public static string HashPassword(string password)
        {
            // Generate a random salt
            byte[] salt = new byte[128 / 8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Hash the password with PBKDF2 with 10,000 iterations
            string hashedPassword = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 256 / 8));

            // Combine the salt and the hashed password for storage
            return $"{Convert.ToBase64String(salt)}:{hashedPassword}";
        }

        // Verify a password against a stored hash
        public static bool VerifyPassword(string storedHash, string password)
        {
            try
            {
                // Split the stored hash into salt and hash
                var parts = storedHash.Split(':');
                if (parts.Length != 2)
                    return false;

                var salt = Convert.FromBase64String(parts[0]);
                var storedPasswordHash = parts[1];

                // Hash the input password with the stored salt
                string computedHash = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                    password: password,
                    salt: salt,
                    prf: KeyDerivationPrf.HMACSHA256,
                    iterationCount: 10000,
                    numBytesRequested: 256 / 8));

                // Compare the computed hash with the stored hash
                return storedPasswordHash == computedHash;
            }
            catch
            {
                return false;
            }
        }
    }
} 