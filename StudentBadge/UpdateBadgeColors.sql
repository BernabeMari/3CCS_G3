-- SQL to update existing badge colors in the database
-- First, update all 'warning' values to 'needs'
UPDATE StudentDetails SET BadgeColor = 'needs' WHERE BadgeColor = 'warning';

-- Then, update all 'green' values to appropriate values based on score
UPDATE StudentDetails 
SET BadgeColor = CASE
    WHEN Score >= 95 THEN 'platinum'
    WHEN Score >= 85 THEN 'gold'
    WHEN Score >= 75 THEN 'silver'
    WHEN Score >= 65 THEN 'bronze'
    WHEN Score >= 50 THEN 'rising-star'
    WHEN Score >= 1 THEN 'needs'
    ELSE 'none'
END
WHERE BadgeColor = 'green';

-- Finally, run a comprehensive update to ensure all badge colors match scores
UPDATE StudentDetails 
SET BadgeColor = CASE
    WHEN Score >= 95 THEN 'platinum'
    WHEN Score >= 85 THEN 'gold'
    WHEN Score >= 75 THEN 'silver'
    WHEN Score >= 65 THEN 'bronze'
    WHEN Score >= 50 THEN 'rising-star'
    WHEN Score >= 1 THEN 'needs'
    ELSE 'none'
END;

-- Also update the Students table for backward compatibility
UPDATE Students 
SET BadgeColor = CASE
    WHEN Score >= 95 THEN 'platinum'
    WHEN Score >= 85 THEN 'gold'
    WHEN Score >= 75 THEN 'silver'
    WHEN Score >= 65 THEN 'bronze'
    WHEN Score >= 50 THEN 'rising-star'
    WHEN Score >= 1 THEN 'needs'
    ELSE 'none'
END
WHERE BadgeColor IN ('warning', 'green'); 