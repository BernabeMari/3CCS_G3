// Progressive Loader for Employer Dashboard
// This script implements progressive loading to make the UI appear faster

// Add CSS for loading spinner
const style = document.createElement('style');
style.textContent = `
    .loading-container {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        min-height: 200px;
        padding: 2rem;
    }

    .loading-spinner {
        width: 50px;
        height: 50px;
        border: 5px solid rgba(0, 0, 0, 0.1);
        border-radius: 50%;
        border-top-color: #3498db;
        animation: spin 1s ease-in-out infinite;
        margin-bottom: 1rem;
    }

    @keyframes spin {
        to { transform: rotate(360deg); }
    }
    
    .student-item {
        animation: fadeIn 0.3s ease-out;
    }
    
    @keyframes fadeIn {
        from { opacity: 0; transform: translateY(10px); }
        to { opacity: 1; transform: translateY(0); }
    }
`;
document.head.appendChild(style);

// Initialize as soon as possible - before DOM is fully loaded
console.log('Progressive loader initializing...');

// Create a startup sequence that doesn't block the UI
window.addEventListener('load', function() {
    console.log('Window loaded, starting progressive loading sequence');
    
    // Step 1: Initialize UI elements immediately (fast)
    initializeUIElements();
    
    // Step 2: Start loading student data with a small delay to let UI render
    setTimeout(loadStudentData, 100);
});

// Function to initialize UI elements (runs quickly)
function initializeUIElements() {
    console.log('Initializing UI elements');
    
    // Initialize filter buttons
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.addEventListener('click', function() {
            const filterType = this.getAttribute('data-filter');
            
            // Update active button
            document.querySelectorAll('.filter-btn').forEach(b => b.classList.remove('active'));
            this.classList.add('active');
            
            // Call the filter function from main script
            if (typeof filterByCategory === 'function') {
                filterByCategory(filterType);
            } else {
                console.warn('filterByCategory function not available');
            }
        });
    });
    
    // Initialize search box
    const searchButton = document.getElementById('searchButton');
    const searchInput = document.getElementById('studentSearch');
    
    if (searchButton && searchInput) {
        searchButton.addEventListener('click', function() {
            if (typeof searchStudents === 'function') {
                searchStudents();
            } else {
                console.warn('searchStudents function not available');
            }
        });
        
        searchInput.addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                if (typeof searchStudents === 'function') {
                    searchStudents();
                } else {
                    console.warn('searchStudents function not available');
                }
            }
        });
    }
    
    // Initialize other UI elements
    console.log('UI elements initialized and ready');
}

// Function to load student data in a non-blocking way
function loadStudentData() {
    console.log('Starting to load student data');
    const containerElement = document.getElementById('studentContainer');
    
    if (!containerElement) {
        console.error('Student container not found');
        return;
    }
    
    // First try to get data from the hidden JSON element (fastest)
    const studentsDataElement = document.getElementById('studentsData');
    if (studentsDataElement) {
        try {
            const studentsData = JSON.parse(studentsDataElement.textContent);
            console.log(`Parsed ${studentsData.length} students from embedded JSON`);
            
            // Process student data in batches
            processStudentDataInBatches(studentsData);
            return;
        } catch (error) {
            console.error('Error parsing embedded student data:', error);
            // Fall back to API if JSON parsing fails
        }
    }
    
    // Fallback to API
    fetchStudentsFromAPI();
}

// Process student data in batches to keep UI responsive
function processStudentDataInBatches(students, batchSize = 10) {
    const containerElement = document.getElementById('studentContainer');
    if (!containerElement) {
        console.error('Student container element not found');
        return;
    }
    
    // Clear the loading indicator
    containerElement.innerHTML = '';
    
    // Process students in batches
    const totalStudents = students.length;
    let processedCount = 0;
    
    function processBatch() {
        const batchEnd = Math.min(processedCount + batchSize, totalStudents);
        
        for (let i = processedCount; i < batchEnd; i++) {
            const student = students[i];
            
            // Skip invalid students
            if (!student) continue;
            
            const studentElement = createStudentElement(student);
            containerElement.appendChild(studentElement);
        }
        
        processedCount = batchEnd;
        
        // If we've processed all students, update scores and sort
        if (processedCount >= totalStudents) {
            console.log(`Completed loading all ${totalStudents} students`);
            
            // Call main script functions if available
            if (typeof updateStudentScoresInList === 'function') {
                setTimeout(updateStudentScoresInList, 100);
            }
            
            if (typeof sortStudentsByScore === 'function') {
                setTimeout(sortStudentsByScore, 200);
            }
            
            return;
        }
        
        // Otherwise process the next batch
        setTimeout(processBatch, 10); // Small delay to give UI a chance to update
    }
    
    // Start processing the first batch
    setTimeout(processBatch, 0);
}

// Function to create a student element from data
function createStudentElement(student) {
    if (!student) return document.createElement('div');
    
    // Create student item container
    const element = document.createElement('div');
    element.className = `student-item student-pulse-${getBadgeClass(student.score)}`;
    element.setAttribute('data-student-id', student.idNumber || student.studentId || student.IdNumber || '');
    element.setAttribute('data-course', student.course || student.Course || '');
    element.setAttribute('data-grade-level', getGradeLevel(student));
    element.setAttribute('data-achievements', student.achievements || student.Achievements || '');
    element.setAttribute('data-score', student.score || student.Score || 0);
    
    // Set background color based on score
    const score = parseFloat(student.score || student.Score || 0);
    const containerColor = score >= 95 ? "#b9f2ff" :
                          score >= 85 ? "#ffe34f" :
                          score >= 75 ? "#cfcccc" :
                          score >= 65 ? "#f5b06c" :
                          score >= 50 ? "#98fb98" :
                          score >= 1  ? "#ffcccb" : "#ffffff";
    element.style.backgroundColor = containerColor;
    
    // Build student info section
    element.innerHTML = `
        <div class="student-info">
            <h3>${student.fullName || student.FullName || 'Student'}</h3>
            <p>${student.course || student.Course || ''} - ${student.section || student.Section || ''}</p>
            <div class="student-score" data-student-id="${student.idNumber || student.IdNumber || ''}">
                ${score > 0 ? score.toFixed(2) : "0.00"}
                <span class="badge badge-${getBadgeClass(score)}">
                    ${student.badgeColor || student.BadgeColor || 'none'}
                </span>
            </div>
            <div style="font-size: 12px; margin-top: 3px; color: #666;">
                ${getGradeLevelText(student)}
            </div>
        </div>
        <div class="student-actions">
            <button class="action-btn view-profile" onclick="showStudentProfile('${student.idNumber || student.IdNumber || ''}', '${(student.fullName || student.FullName || '').replace(/'/g, "\\'")}')">
                <i class="fas fa-user"></i>
            </button>
            <button class="action-btn chat-btn" onclick="openChat('${student.idNumber || student.IdNumber || ''}', '${(student.fullName || student.FullName || '').replace(/'/g, "\\'")}')">
                <i class="fas fa-comment-alt"></i>
            </button>
            <button class="action-btn video-call-btn" onclick="initiateVideoCall('${student.idNumber || student.IdNumber || ''}', '${(student.fullName || student.FullName || '').replace(/'/g, "\\'")}')">
                <i class="fas fa-video"></i>
            </button>
            <button class="action-btn certificate-btn" onclick="openEdubadgeCertificate('${student.idNumber || student.IdNumber || ''}', '${(student.fullName || student.FullName || '').replace(/'/g, "\\'")}')">
                <i class="fas fa-certificate"></i>
            </button>
            <button class="action-btn mark-student-btn" onclick="markStudent('${student.idNumber || student.IdNumber || ''}', '${(student.fullName || student.FullName || '').replace(/'/g, "\\'")}')">
                <i class="far fa-bookmark"></i>
            </button>
        </div>
    `;

    // Add trigger to update student score after creation
    setTimeout(() => {
        const studentId = student.idNumber || student.IdNumber || student.studentId;
        if (studentId && typeof fetchStudentScore === 'function') {
            fetchStudentScore(studentId, element);
        } else if (studentId) {
            // Fallback if the fetchStudentScore function isn't available yet
            fetch(`/Dashboard/GetStudentScore?studentId=${studentId}`)
                .then(response => response.ok ? response.json() : null)
                .then(data => {
                    if (data && data.success) {
                        const scoreElement = element.querySelector('.student-score');
                        if (scoreElement) {
                            scoreElement.innerHTML = `
                                ${parseFloat(data.score).toFixed(2)} 
                                <span class="badge badge-${getBadgeClass(data.badgeColor || 'warning')}">
                                    ${data.badgeColor || 'warning'}
                                </span>
                            `;
                        }
                        // Update data attributes with individual scores
                        element.setAttribute('data-score', data.score || 0);
                        element.setAttribute('data-academic-score', data.academicScore || 0);
                        element.setAttribute('data-mastery-score', data.masteryScore || 0);
                        element.setAttribute('data-seminars-score', data.seminarsScore || 0);
                        element.setAttribute('data-extracurricular-score', data.extracurricularScore || 0);
                    }
                })
                .catch(err => console.error('Error fetching score:', err));
        }
    }, 100);
    
    return element;
}

// Helper function to get grade level from student object
function getGradeLevel(student) {
    if (!student) return 1;
    
    if (student.gradeLevel !== undefined && student.gradeLevel !== null) {
        return student.gradeLevel;
    }
    
    if (student.GradeLevel !== undefined && student.GradeLevel !== null) {
        return student.GradeLevel;
    }
    
    return 1; // Default to 1st year
}

// Helper function to get grade level text
function getGradeLevelText(student) {
    const gradeLevel = getGradeLevel(student);
    
    if (gradeLevel === 5) {
        return "Graduated";
    } else if (gradeLevel <= 0) {
        return "Year 1";
    } else {
        return `Year ${gradeLevel}`;
    }
}

// Helper function to get badge class from score
function getBadgeClass(score) {
    score = parseFloat(score || 0);
    
    if (score >= 95) return "platinum";
    if (score >= 85) return "gold";
    if (score >= 75) return "silver";
    if (score >= 65) return "bronze";
    if (score >= 50) return "rising";
    if (score >= 1) return "needs";
    return "none";
}

// Fetch students from API as a fallback
function fetchStudentsFromAPI() {
    console.log("Fetching students from API endpoint");
    const containerElement = document.getElementById('studentContainer');
    
    // Make sure the container shows loading state
    if (containerElement && !containerElement.querySelector('.loading-spinner')) {
        containerElement.innerHTML = `
            <div class="loading-container">
                <div class="loading-spinner"></div>
                <p>Loading student data...</p>
            </div>
        `;
    }
    
    fetch('/Dashboard/GetStudentsForEmployer')
        .then(response => {
            if (!response.ok) {
                throw new Error('Server returned ' + response.status);
            }
            return response.json();
        })
        .then(data => {
            if (data.success && data.students && data.students.length > 0) {
                // Process data in batches
                processStudentDataInBatches(data.students);
            } else {
                console.warn('No students found in API response');
                containerElement.innerHTML = '<p>No students found with visible profiles.</p>';
            }
        })
        .catch(error => {
            console.error('Error fetching students from API:', error);
            containerElement.innerHTML = '<p>Error loading student data. Please refresh the page.</p>';
        });
}

console.log('Progressive loader script loaded successfully'); 