# StudentBadge Application

## Database Update

After upgrading to the latest version, you need to update your database schema to support new features:

1. Open SQL Server Management Studio or another SQL client
2. Connect to your database server
3. Run the SQL script located at `StudentBadge/Data/DbUpdateScript.sql`
4. This script will:
   - Add Username and Password columns to the Students table
   - Populate existing students with default credentials (using ID number as username)
   - Display messages about the changes made

⚠️ **Important Security Note:** After running the script, advise students to change their default passwords.

## Student Import Feature

The StudentBadge application allows administrators to import student data from Excel spreadsheets.

### Required Columns

When importing students, your Excel file must contain the following columns in this exact order:

1. **Full Name** - The student's complete name
2. **Username** - A unique login username for the student
3. **Password** - Initial password for the student account
4. **ID Number** - A unique student ID number
5. **Course** - The student's enrolled course/program
6. **Section** - The student's section

**Note:** All fields are required. No empty values are allowed in any column.

### Import Process

1. Login as an administrator
2. Navigate to the Admin Dashboard
3. Click on the "Import Students" tab
4. Download the sample template by clicking "Download Sample Template"
5. Fill in the template with student data
6. Upload the completed template using the "Upload" button
7. Review any error messages

### Validation Rules

The import process checks for the following:

- All fields must be filled (Full Name, Username, Password, ID Number, Course, Section)
- ID numbers must be unique (will not overwrite existing students)
- Usernames must be unique

### Troubleshooting

If you encounter errors during import:
- Check that all columns exist and are in the correct order
- Ensure all fields have values (no empty cells)
- Verify that no duplicate ID numbers or usernames exist
- Make sure to use a .xlsx file format (Excel 2007 or newer)
- Maximum file size is 5MB

For additional assistance, contact technical support. 