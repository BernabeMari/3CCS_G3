     // Function to update student grade levels
     function updateAllGradeLevels() {
        if (!confirm('This will update grade levels for all students. Continue?')) {
            return;
        }
        
        // Show loading notification
        var loadingNotification = document.createElement('div');
        loadingNotification.textContent = 'Updating grade levels...';
        loadingNotification.style.position = 'fixed';
        loadingNotification.style.top = '10px';
        loadingNotification.style.right = '10px';
        loadingNotification.style.backgroundColor = '#3498db';
        loadingNotification.style.color = 'white';
        loadingNotification.style.padding = '10px';
        loadingNotification.style.borderRadius = '5px';
        loadingNotification.style.zIndex = '9999';
        document.body.appendChild(loadingNotification);
        
        // Call the API to update grade levels
        fetch('/Dashboard/UpdateGradeLevels', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        })
        .then(response => response.json())
        .then(data => {
            // Remove loading notification
            document.body.removeChild(loadingNotification);
            
            // Show result notification
            var resultNotification = document.createElement('div');
            resultNotification.textContent = data.success ? 
                'Grade levels updated successfully!' : 
                'Error updating grade levels: ' + data.message;
            resultNotification.style.position = 'fixed';
            resultNotification.style.top = '10px';
            resultNotification.style.right = '10px';
            resultNotification.style.backgroundColor = data.success ? '#2ecc71' : '#e74c3c';
            resultNotification.style.color = 'white';
            resultNotification.style.padding = '10px';
            resultNotification.style.borderRadius = '5px';
            resultNotification.style.zIndex = '9999';
            document.body.appendChild(resultNotification);
            
            // Remove notification after 3 seconds
            setTimeout(() => {
                document.body.removeChild(resultNotification);
                
                // If successful, reload the page to show updated grade levels
                if (data.success) {
                    window.location.reload();
                }
            }, 3000);
        })
        .catch(error => {
            // Remove loading notification
            document.body.removeChild(loadingNotification);
            
            // Show error notification
            var errorNotification = document.createElement('div');
            errorNotification.textContent = 'Error updating grade levels: ' + error.message;
            errorNotification.style.position = 'fixed';
            errorNotification.style.top = '10px';
            errorNotification.style.right = '10px';
            errorNotification.style.backgroundColor = '#e74c3c';
            errorNotification.style.color = 'white';
            errorNotification.style.padding = '10px';
            errorNotification.style.borderRadius = '5px';
            errorNotification.style.zIndex = '9999';
            document.body.appendChild(errorNotification);
            
            // Remove notification after 3 seconds
            setTimeout(() => {
                document.body.removeChild(errorNotification);
            }, 3000);
        });
    }
    
    // Direct function for toggle switch
    function seniorToggleChanged(toggleElement) {
        var showOnlySeniors = toggleElement.checked;
        
        // Create a visual notification
        var notification = document.createElement('div');
        notification.textContent = showOnlySeniors ? 'Showing only 4th year & graduated students' : 'Showing all students';
        notification.style.position = 'fixed';
        notification.style.top = '10px';
        notification.style.right = '10px';
        notification.style.backgroundColor = '#e74c3c';
        notification.style.color = 'white';
        notification.style.padding = '10px';
        notification.style.borderRadius = '5px';
        notification.style.zIndex = '9999';
        document.body.appendChild(notification);
        
        // Remove notification after 3 seconds
        setTimeout(function() {
            document.body.removeChild(notification);
        }, 3000);
        
        // Filter students
        var studentItems = document.querySelectorAll('.student-item');
        var visibleCount = 0;
        
        studentItems.forEach(function(item) {
            var gradeLevel = parseInt(item.getAttribute('data-grade-level') || '0');
            var shouldShow = !showOnlySeniors || gradeLevel >= 4;
            item.style.display = shouldShow ? 'flex' : 'none';
            
            if (shouldShow) {
                visibleCount++;
            }
        });
        
        // Update notification with count
        notification.textContent += ` (${visibleCount} students)`;
    }
    
    // Direct function for debug button
    function debugToggleClicked() {
        // Get all student items
        var studentItems = document.querySelectorAll('.student-item');
        
        // Count by grade level
        var gradeCounts = {0: 0, 1: 0, 2: 0, 3: 0, 4: 0, 5: 0};
        
        studentItems.forEach(function(item) {
            var gradeLevel = parseInt(item.getAttribute('data-grade-level') || '0');
            gradeCounts[gradeLevel] = (gradeCounts[gradeLevel] || 0) + 1;
        });
        
        // Show summary
        var summary = 'Students by grade level:\n';
        summary += '1st year: ' + (gradeCounts[1] || 0) + '\n';
        summary += '2nd year: ' + (gradeCounts[2] || 0) + '\n';
        summary += '3rd year: ' + (gradeCounts[3] || 0) + '\n';
        summary += '4th year: ' + (gradeCounts[4] || 0) + '\n';
        summary += 'Graduated: ' + (gradeCounts[5] || 0) + '\n';
        summary += 'Unknown: ' + (gradeCounts[0] || 0) + '\n';
        
        // Count currently visible students
        var visibleCount = 0;
        studentItems.forEach(function(item) {
            if (item.style.display !== 'none') {
                visibleCount++;
            }
        });
        
        summary += '\nCurrently visible: ' + visibleCount + ' students';
        
        alert(summary);
    }
    
    let currentStudentId = '';
    let currentStudentName = '';
    let currentResumePath = '';

    // Video call variables
    let connection;
    let currentVideoCallStudentId = '';
    let currentVideoCallStatus = 'idle';
    let activeCallId = null;
    
    // Add this function at the beginning of your script to ensure all profile images use GetProfilePicture
    function getStudentProfileImageUrl(studentId) {
        // Check if student ID is valid, if not return blank image
        if (!studentId || studentId.trim() === '') {
            return '/images/blank.jpg';
        }
        // Always use the FileHandler controller for student images
        return `/FileHandler/GetProfilePicture?studentId=${studentId}&t=${Date.now()}`;
    }
    
    function showStudentProfile(studentId, studentName) {
        currentStudentId = studentId;
        currentStudentName = studentName;
        
        // Show loading state
        document.getElementById('studentProfileModal').style.display = 'block';
        document.getElementById('studentName').textContent = 'Loading...';
        
        // Show exit button on mobile
        if (window.innerWidth <= 1200) {
            document.getElementById('mobileExitBtn').style.display = 'flex';
            document.body.classList.add('modal-open');
        }
        
        // Fetch student details
        fetch(`/Dashboard/GetStudentProfile?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student profile data:", data); // Debug
                if (data.success) {
                    // Basic information
                    document.getElementById('studentName').textContent = data.fullName;
                    document.getElementById('studentDetails').textContent = `${data.course} - ${data.section}`;
                    document.getElementById('studentScore').textContent = `Score: ${data.score ? data.score.toFixed(2) : '0'}`;
                    
                    // Badge and ranking
                    const badgeElement = document.getElementById('studentBadge');
                    if (badgeElement) {
                        // If badge color is white or not set, use warning as fallback
                        let badgeColor = (data.badgeColor && data.badgeColor.toLowerCase() !== 'white') 
                            ? data.badgeColor.toLowerCase() 
                            : 'warning';
                            
                        badgeElement.textContent = `Badge: ${badgeColor}`;
                        badgeElement.style.backgroundColor = (badgeColor === 'platinum') ? '#b9f2ff' : 
                            (badgeColor === 'gold') ? '#ffe34f' : 
                            (badgeColor === 'silver') ? '#cfcccc' : 
                            (badgeColor === 'bronze') ? '#f5b06c' : 
                            (badgeColor === 'rising-star') ? '#98fb98' : 
                            '#ffcccb'; // warning color
                        badgeElement.style.color = (badgeColor === 'platinum' || badgeColor === 'gold' || 
                             badgeColor === 'silver' || badgeColor === 'bronze' || 
                             badgeColor === 'rising-star') ? '#333' : '#333';
                    }
                    
                    // Determine ranking based on score
                    let rankingText = "";
                    if (data.score >= 90) rankingText = "top 10%";
                    else if (data.score >= 80) rankingText = "top 25%";
                    else if (data.score >= 70) rankingText = "top 50%";
                    else rankingText = "below average";
                    document.getElementById('studentRanking').textContent = rankingText;

                    // Challenges
                    const challengesContainer = document.getElementById('studentChallenges');
                    if (challengesContainer) {
                        if (data.completedChallenges && Array.isArray(data.completedChallenges) && data.completedChallenges.length > 0) {
                            try {
                                challengesContainer.innerHTML = data.completedChallenges.map(challenge => `
                                    <div class="challenge-item">
                                        <h4>${challenge.challengeName || 'Unnamed Challenge'}</h4>
                                        <p><strong>Language:</strong> ${challenge.programmingLanguage || 'Not specified'}</p>
                                        <p><strong>Score:</strong> ${challenge.percentageScore ? challenge.percentageScore : 0}%</p>
                                        <p><strong>Completed:</strong> ${challenge.submissionDate || 'Not specified'}</p>
                                        <p>${challenge.description || ''}</p>
                                    </div>
                                `).join('');
                            } catch (error) {
                                console.error('Error rendering challenges:', error);
                                challengesContainer.innerHTML = '<p>Error displaying challenges. Please try again later.</p>';
                            }
                        } else {
                            challengesContainer.innerHTML = '<p>No completed challenges yet.</p>';
                        }
                    }

                    // Achievements
                    const achievementsElement = document.getElementById('studentAchievements');
                    if (achievementsElement) {
                        if (data.achievements) {
                            // Split achievements by commas, pipes or newlines
                            const achievementsList = data.achievements.split(/[,\n|]+/).filter(a => a.trim() !== "");
                            if (achievementsList.length > 0) {
                                let html = '<ul class="achievements-list">';
                                achievementsList.forEach(achievement => {
                                    html += `<li><i class="fas fa-trophy" style="color: gold; margin-right: 8px;"></i>${achievement.trim()}</li>`;
                                });
                                html += '</ul>';
                                achievementsElement.innerHTML = html;
                            } else {
                                achievementsElement.innerHTML = '<p>No specific achievements listed.</p>';
                            }
                        } else {
                            achievementsElement.innerHTML = '<p>No achievements listed yet.</p>';
                        }
                    }
                    
                    // Comments
                    const commentsElement = document.getElementById('studentComments');
                    if (commentsElement) {
                        if (data.comments) {
                            // Split comments by pipes
                            const commentsList = data.comments.split('|').filter(c => c.trim() !== "");
                            if (commentsList.length > 0) {
                                let html = '';
                                commentsList.forEach(comment => {
                                    html += `<p><i class="fas fa-comment" style="color: #4CAF50; margin-right: 8px;"></i>${comment.trim()}</p>`;
                                });
                                commentsElement.innerHTML = html;
                            } else {
                                commentsElement.innerHTML = '<p>No comments available.</p>';
                            }
                        } else {
                            commentsElement.innerHTML = '<p>No teacher comments yet.</p>';
                        }
                    }
                    
                    // Set profile picture
                    const profilePic = document.getElementById('studentProfilePic');
                    if (profilePic) {
                        profilePic.src = getStudentProfileImageUrl(studentId);
                        profilePic.onerror = function() {
                            this.src = '/images/blank.jpg';
                        };
                    }
                    
                    // Handle resume
                    const resumeSection = document.getElementById('studentResume');
                    const viewResumeBtn = document.getElementById('viewResumeBtn');
                    
                    if (resumeSection && viewResumeBtn) {
                        if (data.isResumeVisible && data.resume) {
                            resumeSection.innerHTML = '<p><i class="fas fa-file-alt" style="color: #4CAF50; margin-right: 8px;"></i>Resume is available for viewing</p>';
                            viewResumeBtn.style.display = 'inline-block';
                            viewResumeBtn.onclick = function() {
                                // If we have base64 data in the data.resume field
                                if (data.resume && data.resume.startsWith('data:')) {
                                    // Create a blob from base64 data and create object URL
                                    const parts = data.resume.split(',');
                                    const base64Data = parts.length > 1 ? parts[1] : data.resume;
                                    const contentType = parts[0].includes(':') ? parts[0].split(':')[1].split(';')[0] : 'application/pdf';
                                    
                                    const byteCharacters = atob(base64Data);
                                    const byteNumbers = new Array(byteCharacters.length);
                                    for (let i = 0; i < byteCharacters.length; i++) {
                                        byteNumbers[i] = byteCharacters.charCodeAt(i);
                                    }
                                    const byteArray = new Uint8Array(byteNumbers);
                                    const blob = new Blob([byteArray], { type: contentType });
                                    const fileUrl = URL.createObjectURL(blob);
                                    
                                    // Set iframe source to blob URL
                                    document.getElementById('resumeViewFrame').src = fileUrl;
                                    document.getElementById('resumeViewModal').style.display = 'flex';
                                    document.body.classList.add('modal-open');
                                } else {
                                    // Use our new function to show the resume in the modal iframe
                                    viewResumeInModal(studentId);
                                }
                            };
                        } else {
                            resumeSection.innerHTML = '<p><i class="fas fa-file-alt" style="color: #999; margin-right: 8px;"></i>Resume not available</p>';
                            viewResumeBtn.style.display = 'none';
                        }
                    }
                    
                    // Set profile picture
                    const profilePicUrl = getStudentProfileImageUrl(studentId);
                    const profilePicElement = document.getElementById('studentProfilePic');
                    profilePicElement.src = profilePicUrl;
                    profilePicElement.onerror = function() {
                        this.src = '/images/blank.jpg';
                    };
                    
                    // Load additional student data
                    loadStudentProfile(studentId);
                    
                    // Load score breakdown
                    loadScoreBreakdown(studentId);
                } else {
                    // Handle error
                    document.getElementById('studentName').textContent = 'Error loading profile';
                    console.error('Failed to load student profile:', data.message);
                }
            })
            .catch(error => {
                console.error('Error fetching student profile:', error);
                document.getElementById('studentName').textContent = 'Error loading profile';
            });
    }

    function closeStudentProfile() {
        document.getElementById('studentProfileModal').style.display = 'none';
        document.getElementById('mobileExitBtn').style.display = 'none';
        document.body.classList.remove('modal-open');
    }

    // Close modal when clicking outside
    window.onclick = function(event) {
        const profileModal = document.getElementById('studentProfileModal');
        const chatModal = document.getElementById('chatModal');
        
        if (event.target == profileModal) {
            closeStudentProfile();
        }
        
        if (event.target == chatModal) {
            closeChat();
        }
    }

    // Start chat from profile
    function startChatFromProfile() {
        closeStudentProfile();
        openChat(currentStudentName, currentStudentId);
    }

    function viewResume(studentId) {
        if (currentResumePath) {
            window.open(currentResumePath, '_blank');
        } else {
            alert('Resume is not available for this student.');
        }
    }

    function openChat(studentId, studentName) {
        console.log(`Opening chat for student ID: ${studentId}, Name: ${studentName}`);
        
        currentStudentId = studentId;
        currentStudentName = studentName || 'Student';
        document.getElementById('chatModal').style.display = 'flex';
        document.getElementById('chatStudentName').textContent = currentStudentName;
        document.getElementById('chatStudentCourse').textContent = 'Loading student information...';
        
        // Load student course and avatar
        fetch(`/Dashboard/GetStudentBasicInfo?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student basic info:", data); // Debug
                if (data.success && data.student) {
                    // Make sure we have a student name displayed
                    if (data.student.fullName && data.student.fullName.trim() !== '') {
                        document.getElementById('chatStudentName').textContent = data.student.fullName;
                        currentStudentName = data.student.fullName;
                    }
                    
                    // Set course information or a default
                    const courseText = (data.student.course || '') + 
                        (data.student.section ? ' - ' + data.student.section : '');
                        
                    document.getElementById('chatStudentCourse').textContent = 
                        courseText.trim() !== '' ? courseText : 'No course information';
                    
                    // Set profile picture
                    const avatarElement = document.getElementById('chatStudentAvatar');
                    avatarElement.src = getStudentProfileImageUrl(studentId);
                    avatarElement.onerror = function() {
                        this.src = '/images/blank.jpg';
                    };
                } else {
                    console.error('Student data missing or incomplete:', data);
                    document.getElementById('chatStudentCourse').textContent = 'Student information unavailable';
                }
            })
            .catch(error => {
                console.error('Error fetching student info:', error);
                document.getElementById('chatStudentAvatar').src = '/images/blank.jpg';
                document.getElementById('chatStudentCourse').textContent = 'Error loading student information';
            });
        
        // Show exit button on mobile
        if (window.innerWidth <= 1200) {
            document.body.classList.add('modal-open');
        }
        
        // Clear previous messages and show loading state
        const chatMessages = document.getElementById('chatMessages');
        chatMessages.innerHTML = '<div style="text-align: center; padding: 20px;">Loading messages...</div>';
        
        // Load message history
        loadMessageHistory(studentId);
        
        // Focus on input after messages are loaded
        setTimeout(() => {
            document.getElementById('messageInput').focus();
        }, 500);
        
        // Close the messages dropdown if open
        document.getElementById('messagesDropdown').classList.remove('active');
    }

    function loadMessageHistory(studentId) {
        fetch(`/Dashboard/GetEmployerMessageHistory?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                if (data.success) {
                    const chatMessages = document.getElementById('chatMessages');
                    chatMessages.innerHTML = '';
                    
                    if (data.messages && data.messages.length > 0) {
                        data.messages.forEach(message => {
                            const messageDiv = document.createElement('div');
                            messageDiv.className = `message ${message.isFromEmployer ? 'sent' : 'received'}`;
                            
                            const contentDiv = document.createElement('div');
                            contentDiv.className = 'message-content';
                            contentDiv.textContent = message.content;
                            
                            const timeDiv = document.createElement('div');
                            timeDiv.className = 'message-time';
                            timeDiv.textContent = new Date(message.sentTime).toLocaleString();
                            
                            messageDiv.appendChild(contentDiv);
                            messageDiv.appendChild(timeDiv);
                            chatMessages.appendChild(messageDiv);
                        });
                    } else {
                        chatMessages.innerHTML = '<div style="text-align: center; padding: 20px; color: #666;">No messages yet. Start a conversation!</div>';
                    }
                    
                    // Scroll to bottom of chat
                    chatMessages.scrollTop = chatMessages.scrollHeight;
                } else {
                    chatMessages.innerHTML = '<div style="text-align: center; padding: 20px; color: #d32f2f;">Failed to load messages. Please try again.</div>';
                }
            })
            .catch(error => {
                console.error('Error loading messages:', error);
                document.getElementById('chatMessages').innerHTML = '<div style="text-align: center; padding: 20px; color: #d32f2f;">Error loading messages. Please try again.</div>';
            });
    }
    
    function sendMessage() {
        const messageInput = document.getElementById('messageInput');
        const message = messageInput.value.trim();
        
        if (message && currentStudentId) {
            // Disable input while sending
            messageInput.disabled = true;
            const sendButton = messageInput.nextElementSibling;
            sendButton.disabled = true;
            
            // Add message to UI immediately for better UX
            const chatMessages = document.getElementById('chatMessages');
            const messageDiv = document.createElement('div');
            messageDiv.className = 'message sent';
            
            const contentDiv = document.createElement('div');
            contentDiv.className = 'message-content';
            contentDiv.textContent = message;
            
            const timeDiv = document.createElement('div');
            timeDiv.className = 'message-time';
            timeDiv.textContent = new Date().toLocaleString();
            
            messageDiv.appendChild(contentDiv);
            messageDiv.appendChild(timeDiv);
            chatMessages.appendChild(messageDiv);
            chatMessages.scrollTop = chatMessages.scrollHeight;
            
            // Clear input
            messageInput.value = '';
            
            // Send to server
            fetch('/Dashboard/SendMessage', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    studentId: currentStudentId,
                    message: message
                })
            })
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                if (!data.success) {
                    const errorDiv = document.createElement('div');
                    errorDiv.style.color = '#d32f2f';
                    errorDiv.style.fontSize = '12px';
                    errorDiv.style.textAlign = 'center';
                    errorDiv.style.padding = '10px';
                    errorDiv.textContent = 'The student has turned off their chat. Please wait until they turn it back on.';
                    chatMessages.appendChild(errorDiv);
                    chatMessages.scrollTop = chatMessages.scrollHeight;
                }
                messageInput.disabled = false;
                sendButton.disabled = false;
                messageInput.focus();
            })
            .catch(error => {
                console.error('Error sending message:', error);
                const errorDiv = document.createElement('div');
                errorDiv.style.color = '#d32f2f';
                errorDiv.style.fontSize = '12px';
                errorDiv.style.textAlign = 'center';
                errorDiv.style.padding = '10px';
                errorDiv.textContent = 'Error sending message. Please try again.';
                chatMessages.appendChild(errorDiv);
                chatMessages.scrollTop = chatMessages.scrollHeight;
                
                messageInput.disabled = false;
                sendButton.disabled = false;
                messageInput.focus();
            });
        }
    }

    function closeChat() {
        document.getElementById('chatModal').style.display = 'none';
        document.getElementById('mobileExitBtn').style.display = 'none';
        document.body.classList.remove('modal-open');
        currentStudentId = '';
    }

    function filterStudents(course) {
        // Update active button
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.textContent.includes(course === 'all' ? 'All Students' : course)) {
                btn.classList.add('active');
            }
        });

        // Get all student items
        const studentItems = document.querySelectorAll('.student-item');
        
        studentItems.forEach(item => {
            if (course === 'all') {
                item.style.display = 'flex';
            } else {
                const studentCourse = item.getAttribute('data-course');
                item.style.display = studentCourse.includes(course) ? 'flex' : 'none';
            }
        });

        // Sort the visible students by score (top performers first)
        sortStudentsByScore();
    }

    // Function to sort students by score
    function sortStudentsByScore() {
        const studentContainer = document.getElementById('studentContainer');
        const studentItems = Array.from(document.querySelectorAll('.student-item'));
        
        // Filter for only visible students
        const visibleStudents = studentItems.filter(item => item.style.display !== 'none');
        
        // Sort by score (highest first)
        visibleStudents.sort((a, b) => {
            // Extract the score value as a decimal
            const scoreA = parseFloat(a.querySelector('.student-score').textContent.trim());
            const scoreB = parseFloat(b.querySelector('.student-score').textContent.trim());
            return scoreB - scoreA;
        });
        
        // Reappend sorted items
        visibleStudents.forEach(item => studentContainer.appendChild(item));
    }

    // Initialize when document is ready
    document.addEventListener('DOMContentLoaded', function() {
        loadPreviousChats();
        sortStudentsByScore();
    });

    // Function to check for student replies
    function checkStudentReplies() {
        const employerId = '@ViewBag.EmployerId';
        fetch(`/Dashboard/GetEmployerMessages?employerId=${employerId}`)
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    // Create a map of student IDs who have replied
                    const repliedStudents = new Set(data.messages.map(m => m.studentId));
                    
                    // Update student items in the list
                    document.querySelectorAll('.student-item').forEach(item => {
                        const studentId = item.getAttribute('data-student-id');
                        if (repliedStudents.has(studentId)) {
                            item.classList.add('has-replied');
                        } else {
                            item.classList.remove('has-replied');
                        }
                    });
                }
            })
            .catch(error => console.error('Error checking student replies:', error));
    }

    // Check for student replies periodically
    setInterval(checkStudentReplies, 30000); // Check every 30 seconds

    // Initial check for student replies
    checkStudentReplies();

    function toggleSidebar() {
        const sidebar = document.querySelector('.sidebar');
        sidebar.classList.toggle('active');
    }

    // Close sidebar when clicking outside
    document.addEventListener('click', function(event) {
        const sidebar = document.querySelector('.sidebar');
        const mobileMenuToggle = document.querySelector('.mobile-menu-toggle');
        
        if (!sidebar.contains(event.target) && !mobileMenuToggle.contains(event.target) && window.innerWidth <= 1200) {
            sidebar.classList.remove('active');
        }
    });

    // Handle window resize
    window.addEventListener('resize', function() {
        const sidebar = document.querySelector('.sidebar');
        if (window.innerWidth > 1200) {
            sidebar.classList.remove('active');
            document.getElementById('mobileExitBtn').style.display = 'none';
            document.body.classList.remove('modal-open');
        }
    });

    // New function to handle the exit button
    function exitCurrentView() {
        // Close any open modals or panels
        closeStudentProfile();
        closeChat();
        
        // Close sidebar if open
        const sidebar = document.querySelector('.sidebar');
        if (sidebar.classList.contains('active')) {
            sidebar.classList.remove('active');
        }
        
        // Hide the exit button
        document.getElementById('mobileExitBtn').style.display = 'none';
        document.body.classList.remove('modal-open');
    }

    // New function to toggle messages dropdown
    function toggleMessagesDropdown() {
        const dropdown = document.getElementById('messagesDropdown');
        dropdown.classList.toggle('active');
        
        if (dropdown.classList.contains('active')) {
            loadPreviousChats();
        }
    }
    
    // Load previous chats
    function loadPreviousChats() {
        const previousChats = document.getElementById('previousChats');
        previousChats.innerHTML = '<div class="loading-chats" style="padding: 20px; text-align: center; color: #666;">Loading previous chats...</div>';
        
        const employerId = '@ViewBag.EmployerId';
        console.log("Fetching chats for employer:", employerId); // Debug
        
        fetch(`/Dashboard/GetEmployerChats?employerId=${employerId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok: ' + response.statusText);
                }
                return response.json();
            })
            .then(data => {
                console.log("Received chats data:", data); // Debug
                if (data.success && data.chats && data.chats.length > 0) {
                    previousChats.innerHTML = '';
                    
                    data.chats.forEach(chat => {
                        const chatItem = document.createElement('div');
                        chatItem.className = 'previous-chat-item';
                        chatItem.onclick = function() {
                            openChat(chat.studentId, chat.name);
                        };
                        
                        const avatar = document.createElement('img');
                        avatar.className = 'previous-chat-avatar';
                        avatar.src = getStudentProfileImageUrl(chat.studentId);
                        avatar.onerror = function() { this.src = '/images/blank.jpg'; };
                        
                        const infoDiv = document.createElement('div');
                        infoDiv.className = 'previous-chat-info';
                        
                        const nameDiv = document.createElement('div');
                        nameDiv.className = 'previous-chat-name';
                        nameDiv.textContent = chat.name || 'Unknown Student';
                        
                        const messageDiv = document.createElement('div');
                        messageDiv.className = 'previous-chat-last-message';
                        messageDiv.textContent = chat.recentMessage ? chat.recentMessage.content : 'No messages yet';
                        
                        const timeDiv = document.createElement('div');
                        timeDiv.className = 'previous-chat-time';
                        
                        if (chat.recentMessage && chat.recentMessage.sentTime) {
                            try {
                                const date = new Date(chat.recentMessage.sentTime);
                                if (!isNaN(date.getTime())) {
                                    timeDiv.textContent = date.toLocaleDateString();
                                } else {
                                    timeDiv.textContent = ''; // Invalid date, don't show anything
                                }
                            } catch (e) {
                                console.error('Error formatting date:', e);
                                timeDiv.textContent = '';
                            }
                        } else {
                            timeDiv.textContent = '';
                        }
                        
                        infoDiv.appendChild(nameDiv);
                        infoDiv.appendChild(messageDiv);
                        
                        chatItem.appendChild(avatar);
                        chatItem.appendChild(infoDiv);
                        chatItem.appendChild(timeDiv);
                        
                        previousChats.appendChild(chatItem);
                    });
                } else {
                    previousChats.innerHTML = '<div style="padding: 20px; text-align: center; color: #666;">No previous conversations found</div>';
                }
            })
            .catch(error => {
                console.error('Error loading previous chats:', error);
                previousChats.innerHTML = '<div style="padding: 20px; text-align: center; color: #f44336;">Error loading conversations: ' + error.message + '</div>';
            });
    }
    
    // Close dropdown when clicking outside
    document.addEventListener('click', function(event) {
        const dropdown = document.getElementById('messagesDropdown');
        const icon = document.querySelector('.messages-icon');
        
        if (!dropdown.contains(event.target) && !icon.contains(event.target) && dropdown.classList.contains('active')) {
            dropdown.classList.remove('active');
        }
    });

    // Debug function to check if we have the proper employer ID
    document.addEventListener('DOMContentLoaded', function() {
        console.log("Employer ID from ViewBag:", '@ViewBag.EmployerId');
        
        // Add Enter key event listener for the message input
        document.getElementById('messageInput').addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                sendMessage();
            }
        });
    });

    // Utility function to determine text color based on background
    function getContrastTextColor(bgColor) {
        // Default to white text if can't determine
        if (!bgColor || typeof bgColor !== 'string') return 'white';
        
        const colorMap = {
            'gold': 'black',
            'silver': 'black',
            'bronze': 'black',
            'green': 'white',
            'blue': 'white',
            'red': 'white',
            'purple': 'white',
            'black': 'white',
            'yellow': 'black',
            'orange': 'black',
            'white': 'black',
            'grey': 'white',
            'gray': 'white'
        };
        
        // Try to match common color names
        for (let color in colorMap) {
            if (bgColor.includes(color)) {
                return colorMap[color];
            }
        }
        
        // If no match, default to white
        return 'white';
    }

    // Function to filter and show only top performers (highest scores)
    function filterTopPerformers() {
        // Update active button
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.textContent.includes('Top Performers')) {
                btn.classList.add('active');
            }
        });

        // Get all student items and show all
        const studentItems = document.querySelectorAll('.student-item');
        studentItems.forEach(item => {
            item.style.display = 'flex';
        });

        // Sort by score (highest first)
        sortStudentsByScore();
        
        // Then limit to only show top 10 students
        const studentContainer = document.getElementById('studentContainer');
        const visibleStudents = Array.from(studentItems);
        
        // Hide all students first
        visibleStudents.forEach(item => {
            item.style.display = 'none';
        });
        
        // Show only top 10
        for (let i = 0; i < Math.min(10, visibleStudents.length); i++) {
            visibleStudents[i].style.display = 'flex';
            // Highlight top performers with a gold border
            if (i < 3) {
                visibleStudents[i].style.borderLeft = '4px solid gold';
            }
        }
    }

    // Initialize SignalR connection for video calls
    async function setupSignalR() {
        try {
            console.log("Setting up SignalR connection...");
            
            // Get employer ID before establishing connection
            let employerId;
            
            // Try multiple options to get the employer ID with better logging
            if (document.getElementById('employerId') && document.getElementById('employerId').value) {
                employerId = document.getElementById('employerId').value;
                console.log("Found employer ID from hidden input:", employerId);
            } else if (document.body.getAttribute('data-employer-id')) {
                employerId = document.body.getAttribute('data-employer-id');
                console.log("Found employer ID from body attribute:", employerId);
            } else if (window.employerId) {
                employerId = window.employerId;
                console.log("Found employer ID from global variable:", employerId);
            } else {
                console.warn("Could not find employer ID from any source. Video calls may not work correctly.");
            }
            
            // Skip SignalR setup if employer ID isn't found and don't block the page load
            if (!employerId || employerId === "@jsEmployerId") {
                console.warn("No valid employer ID found. Skipping SignalR setup to avoid connection errors.");
                return;
            }
            
            // Set up connection with timeout to avoid hanging indefinitely
            const connectionPromise = new Promise((resolve, reject) => {
                // Create SignalR connection
                connection = new signalR.HubConnectionBuilder()
                    .withUrl("/videoCallHub")
                    .withAutomaticReconnect()
                    .build();
                
                // Handle call response from student
                connection.on("CallResponse", (callId, status) => {
                    handleCallResponse(callId, status);
                });
                
                // Handle call requested confirmation
                connection.on("CallRequested", (callId) => {
                    activeCallId = callId;
                    document.getElementById('videoCallStatus').textContent = 'Call request sent. Waiting for student to respond...';
                    document.getElementById('startCallBtn').style.display = 'none';
                    document.getElementById('cancelCallBtn').textContent = 'Cancel Call';
                });
                
                // Handle errors
                connection.on("Error", (error) => {
                    console.error("SignalR error:", error);
                    if (document.getElementById('videoCallStatus')) {
                        document.getElementById('videoCallStatus').textContent = `Error: ${error}`;
                    }
                });
                
                // Start the connection with timeout
                const connectionTimeout = setTimeout(() => {
                    reject(new Error("Connection timeout after 10 seconds"));
                }, 10000); // 10 second timeout
                
                connection.start()
                    .then(() => {
                        clearTimeout(connectionTimeout);
                        console.log("SignalR connection established");
                        resolve();
                    })
                    .catch(err => {
                        clearTimeout(connectionTimeout);
                        reject(err);
                    });
            });
            
            // Wait for connection to be established
            await connectionPromise;
            
            // Register the connection only after the connection is established
            console.log(`Registering connection with employer ID: ${employerId}`);
            await connection.invoke("RegisterConnection", employerId, "employer");
            console.log("Connection registered successfully!");
            
        } catch (error) {
            console.error("Error setting up SignalR:", error);
            // Don't block page loading if SignalR setup fails
        }
    }
    
    // Cancel the video call
    async function cancelVideoCall() {
        try {
            if (currentVideoCallStatus === 'calling' && activeCallId) {
                // Check if connection is still active
                if (!connection || connection.state !== "Connected") {
                    console.warn("SignalR connection not established. Cannot properly end call.");
                    closeVideoCallModal();
                    return;
                }
                
                // Get the employer ID the same way as in the startVideoCall function
                let employerId;
                
                // Try multiple options to get the employer ID with better validation
                if (document.getElementById('employerId') && document.getElementById('employerId').value) {
                    employerId = document.getElementById('employerId').value;
                } else if (document.body.getAttribute('data-employer-id')) {
                    employerId = document.body.getAttribute('data-employer-id');
                } else if (window.employerId) {
                    employerId = window.employerId;
                } else {
                    console.warn("Cannot find employer ID. Cannot properly end call.");
                    closeVideoCallModal();
                    return;
                }
                
                // If we don't have a valid employer ID, just close the modal
                if (!employerId || employerId === "@jsEmployerId") {
                    console.warn("Invalid employer ID. Cannot properly end call.");
                    closeVideoCallModal();
                    return;
                }
                
                // End the call if it's in progress with a timeout
                const cancelPromise = connection.invoke("EndCall", activeCallId, employerId, "employer");
                
                // Add a timeout in case the server doesn't respond
                const timeoutPromise = new Promise((_, reject) => {
                    setTimeout(() => reject(new Error("Cancel call timeout")), 5000);
                });
                
                // Race the cancel operation against the timeout
                await Promise.race([cancelPromise, timeoutPromise])
                    .catch(error => {
                        console.error("Error canceling call:", error);
                    });
            }
        } catch (error) {
            console.error("Error canceling call:", error);
        } finally {
            // Always close the modal regardless of errors
            closeVideoCallModal();
        }
    }
    
    // Close the video call modal
    function closeVideoCallModal() {
        document.getElementById('videoCallModal').style.display = 'none';
        currentVideoCallStatus = 'idle';
        activeCallId = null;
    }
    
    // Add event listeners for video call buttons
    document.getElementById('startCallBtn').addEventListener('click', startVideoCall);
    document.getElementById('cancelCallBtn').addEventListener('click', cancelVideoCall);
    
    // Initialize SignalR when document is ready
    document.addEventListener('DOMContentLoaded', function() {
        // Initialize SignalR in a non-blocking way
        setTimeout(() => {
            setupSignalR().catch(err => {
                console.warn("SignalR setup failed but page will continue to load:", err);
            });
        }, 1000); // Delay SignalR initialization by 1 second to prioritize UI rendering
    });

    document.addEventListener('DOMContentLoaded', function() {
        // Initialize tooltips if Bootstrap is used
        if (typeof $().tooltip === 'function') {
            $('[data-toggle="tooltip"]').tooltip();
        }
        
        // Initialize chat modal
        setupChatModal();
        
        // Setup search functionality
        setupSearch();
        
        // Initialize SignalR connection for real-time chat
        setupSignalR();
        
        // Add event listener for senior toggle
        const seniorToggle = document.getElementById('seniorToggle');
        if (seniorToggle) {
            console.log('Senior toggle found, adding event listener');
            seniorToggle.addEventListener('change', function() {
                console.log('Toggle changed, checked:', this.checked);
                applyCurrentFilters();
            });
        } else {
            console.error('Senior toggle element not found!');
        }
        
        // Debug button
        const debugBtn = document.getElementById('debugToggle');
        if (debugBtn) {
            debugBtn.addEventListener('click', function() {
                debugStudentFilters();
            });
        }
    });
    
    // Function to set up the search functionality
    function setupSearch() {
        // Handle search button click
        document.getElementById('searchButton').addEventListener('click', function() {
            searchStudents();
        });
        
        // Handle enter key in search box
        document.getElementById('studentSearch').addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                searchStudents();
            }
        });
    }
    
    // Function to search students by name, achievements, and comments
    function searchStudents() {
        const searchText = document.getElementById('studentSearch').value.toLowerCase();
        
        if (searchText.trim() === '') {
            // If search is empty, reset to current filter
            const activeFilter = document.querySelector('.filter-btn.active');
            if (activeFilter) {
                if (activeFilter.textContent.includes('All Students')) {
                    filterStudents('all');
                } else if (activeFilter.textContent.includes('Top Performers')) {
                    filterTopPerformers();
                } else {
                    // Get the course code from the button text
                    const courseCode = activeFilter.textContent.trim();
                    filterStudents(courseCode);
                }
            } else {
                filterStudents('all');
            }
            return;
        }
        
        // Show loading indicator
        showNotification('info', 'Searching students...');
        
        // Remove active class from all filter buttons
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
        });
        
        // Get all student items for searching
        const studentItems = document.querySelectorAll('.student-item');
        const seniorToggle = document.getElementById('seniorToggle');
        const showOnlySeniors = seniorToggle.checked;
        
        // First search by student name which is already available
        let foundMatch = false;
        let matchedElements = [];
        
        studentItems.forEach(item => {
            const studentName = item.querySelector('h3').textContent.toLowerCase();
            let shouldDisplay = studentName.includes(searchText);
            
            // Apply senior filter if enabled
            if (shouldDisplay && showOnlySeniors) {
                const gradeLevel = parseInt(item.getAttribute('data-grade-level') || '0');
                shouldDisplay = gradeLevel >= 4; // 4th year or graduated (5)
            }
            
            if (shouldDisplay) {
                item.style.display = 'flex';
                matchedElements.push(item);
                foundMatch = true;
            } else {
                item.style.display = 'none';
            }
        });
        
        // Fetch all student data even if we found matches by name
        // Get all student IDs for students not already matched
        const fetchPromises = [];
        studentItems.forEach(item => {
            if (!matchedElements.includes(item)) {
                const studentId = item.getAttribute('data-student-id');
                if (studentId) {
                    const promise = fetchStudentData(studentId).then(data => {
                        // Convert to lowercase strings for case-insensitive search
                        const achievements = (data.achievements || '').toLowerCase();
                        const comments = (data.comments || '').toLowerCase();
                        
                        // Debug output
                        console.log(`Student ${studentId}:`, {
                            searchText,
                            achievements: achievements ? achievements.substring(0, 50) + "..." : "(empty)",
                            comments: comments ? comments.substring(0, 50) + "..." : "(empty)",
                            achievementsMatch: achievements.includes(searchText),
                            commentsMatch: comments.includes(searchText)
                        });
                        
                        // Check if search term is in achievements or comments
                        let shouldDisplay = achievements.includes(searchText) || comments.includes(searchText);
                        
                        // Apply senior filter if enabled
                        if (shouldDisplay && showOnlySeniors) {
                            const gradeLevel = parseInt(item.getAttribute('data-grade-level') || '0');
                            shouldDisplay = gradeLevel >= 4; // 4th year or graduated (5)
                        }
                        
                        if (shouldDisplay) {
                            item.style.display = 'flex';
                            matchedElements.push(item);
                            foundMatch = true;
                            console.log(`Match found for student ${studentId} in ${achievements.includes(searchText) ? 'achievements' : 'comments'}`);
                        }
                        
                        return shouldDisplay;
                    });
                    fetchPromises.push(promise);
                }
            }
        });
        
        // Process all the fetch promises
        Promise.all(fetchPromises).then(results => {
            // If we found matches by name or achievements/comments, show the count
            if (foundMatch) {
                showNotification('success', `Found ${matchedElements.length} student(s) matching "${searchText}"`);
            } else {
                showNotification('info', 'No students found matching your search criteria');
            }
            
            // Sort the results by score
            sortStudentsByScore();
        }).catch(error => {
            console.error("Error during search:", error);
            showNotification('error', 'An error occurred during search');
        });
    }
    
    // Function to fetch student data
    function fetchStudentData(studentId) {
        console.log(`Fetching data for student ${studentId}...`);
        return fetch(`/Dashboard/GetStudentProfileForEmployer?studentId=${studentId}`)
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    console.log("Fetched student data for ID", studentId, ":", data.student);
                    
                    // Get achievements and comments from the student object
                    const achievements = data.student && data.student.Achievements ? data.student.Achievements : '';
                    const comments = data.student && data.student.Comments ? data.student.Comments : '';
                    
                    console.log(`Student ${studentId} data:`, {
                        achievements: achievements ? achievements.substring(0, 50) + "..." : "(empty)",
                        comments: comments ? comments.substring(0, 50) + "..." : "(empty)"
                    });
                    
                    return {
                        achievements: achievements,
                        comments: comments
                    };
                } else {
                    console.warn(`Failed to fetch student ${studentId} data:`, data.message);
                }
                return { achievements: '', comments: '' };
            })
            .catch((error) => {
                console.error("Error fetching student data:", error);
                return { achievements: '', comments: '' };
            });
    }
    
    // Existing filter functions
    function filterStudents(course) {
        // Update active button
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.textContent.includes(course === 'all' ? 'All Students' : course)) {
                btn.classList.add('active');
            }
        });

        // Get all student items
        const studentItems = document.querySelectorAll('.student-item');
        
        studentItems.forEach(item => {
            if (course === 'all') {
                item.style.display = 'flex';
            } else {
                const studentCourse = item.getAttribute('data-course');
                item.style.display = studentCourse.includes(course) ? 'flex' : 'none';
            }
        });

        // Sort the visible students by score (top performers first)
        sortStudentsByScore();
    }

    function filterTopPerformers() {
        // Update active button
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.textContent.includes('Top Performers')) {
                btn.classList.add('active');
            }
        });
        
        // Get all student items
        const studentItems = document.querySelectorAll('.student-item');
        
        // Show all students
        studentItems.forEach(item => {
            item.style.display = 'flex';
        });
        
        // Sort by score
        sortStudentsByScore();
        
        // Only keep top 10 visible
        const visibleStudents = Array.from(studentItems).filter(item => item.style.display !== 'none');
        visibleStudents.forEach((item, index) => {
            if (index >= 10) {
                item.style.display = 'none';
            }
        });
    }
    
    // Sort function for student scores
    function sortStudentsByScore() {
        const studentContainer = document.getElementById('studentContainer');
        const studentItems = Array.from(document.querySelectorAll('.student-item'));
        
        // Filter for only visible students
        const visibleStudents = studentItems.filter(item => item.style.display !== 'none');
        
        // Sort by score (highest first)
        visibleStudents.sort((a, b) => {
            // Extract the score value as a decimal
            const scoreA = parseFloat(a.querySelector('.student-score').textContent.trim());
            const scoreB = parseFloat(b.querySelector('.student-score').textContent.trim());
            return scoreB - scoreA;
        });
        
        // Reappend sorted items
        visibleStudents.forEach(item => studentContainer.appendChild(item));
    }
    
    // Show notifications
    function showNotification(type, message) {
        // Create notification element if it doesn't exist
        let notification = document.getElementById('notification');
        if (!notification) {
            notification = document.createElement('div');
            notification.id = 'notification';
            notification.style.position = 'fixed';
            notification.style.top = '20px';
            notification.style.right = '20px';
            notification.style.padding = '15px 20px';
            notification.style.borderRadius = '5px';
            notification.style.color = 'white';
            notification.style.zIndex = '9999';
            notification.style.transition = 'opacity 0.3s';
            document.body.appendChild(notification);
        }
        
        // Set notification color based on type
        notification.style.backgroundColor = 
            type === 'success' ? '#4CAF50' : 
            type === 'error' ? '#F44336' : 
            type === 'warning' ? '#FF9800' : '#2196F3';
        
        notification.textContent = message;
        notification.style.opacity = '1';
        
        // Hide after 3 seconds
        setTimeout(() => {
            notification.style.opacity = '0';
        }, 3000);
    }
    
    // Chat-related functions
    function setupChatModal() {
        // ... existing code ...
    }

    // Function to load and display student grades
    function loadStudentGrades(studentId) {
        fetch(`/Dashboard/GetStudentBasicInfo?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student grades data:", data);
                
                // Update the grades display
                document.getElementById('FirstYearGrade').textContent = data.FirstYearGrade ? data.FirstYearGrade + '%' : 'Not Available';
                document.getElementById('SecondYearGrade').textContent = data.SecondYearGrade ? data.SecondYearGrade + '%' : 'Not Available';
                document.getElementById('ThirdYearGrade').textContent = data.ThirdYearGrade ? data.ThirdYearGrade + '%' : 'Not Available';
                document.getElementById('FourthYearGrade').textContent = data.FourthYearGrade ? data.FourthYearGrade + '%' : 'Not Available';
            })
            .catch(error => {
                console.error('Error fetching student grades:', error);
                document.getElementById('studentGrades').innerHTML = '<p>Unable to load grade data.</p>';
            });
    }
    
    // Function to load and display student certificates
    function loadStudentCertificates(studentId) {
        fetch(`/ProgrammingTest/GetStudentCertificates?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student certificates:", data);
                const container = $('#studentCertificates');
                
                if (!data || data.length === 0) {
                    container.html('<p>No certificates available.</p>');
                    return;
                }
                
                let html = '<div class="certificates-list">';
                data.forEach(cert => {
                    html += `
                        <div class="certificate-item">
                            <div class="cert-icon"><i class="fas fa-certificate" style="color: gold;"></i></div>
                            <div class="cert-details">
                                <div class="cert-name">${cert.testName}</div>
                                <div class="cert-info">
                                    <span class="cert-lang">${cert.programmingLanguage}</span>
                                    <span class="cert-score">Score: ${cert.score}%</span>
                                    <span class="cert-date">Issued: ${new Date(cert.issueDate).toLocaleDateString()}</span>
                                </div>
                                <div class="cert-actions">
                                    <button class="btn btn-sm btn-primary view-cert-btn" 
                                            onclick="openCertificateModal(${cert.certificateId}, '${cert.testName}')">
                                        View Certificate
                                    </button>
                                </div>
                            </div>
                        </div>
                    `;
                });
                html += '</div>';
                
                container.html(html);
            })
            .catch(error => {
                console.error('Error fetching certificates:', error);
                $('#studentCertificates').html('<p>Error loading certificates. Please try again later.</p>');
            });
    }
    
    // Function to load and display student attendance records (seminars/webinars)
    function loadStudentAttendance(studentId) {
        fetch(`/Dashboard/GetStudentAttendanceRecords?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student attendance data:", data);
                const container = $('#studentSeminars');
                
                if (!data || data.length === 0) {
                    container.html('<p>No seminar or webinar attendance records available.</p>');
                    return;
                }
                
                let html = '<div class="seminars-list">';
                data.forEach(record => {
                    html += `
                        <div class="seminar-item">
                            <div class="seminar-icon"><i class="fas fa-chalkboard-teacher"></i></div>
                            <div class="seminar-details">
                                <div class="seminar-name">${record.eventName}</div>
                                <div class="seminar-desc">${record.eventDescription || ''}</div>
                                <div class="seminar-info">
                                    <span class="seminar-date">Date: ${new Date(record.eventDate).toLocaleDateString()}</span>
                                    <span class="seminar-teacher">Verified by: ${record.teacherName || 'Teacher'}</span>
                                    <span class="seminar-score">Score: ${record.score}</span>
                                </div>
                                <div class="seminar-actions">
                                    ${record.hasProofImage ? 
                                    `<button class="btn btn-sm btn-primary view-proof-btn" 
                                            onclick="openAttendanceProofModal(${record.attendanceId}, '${record.eventName}')">
                                        View Proof
                                    </button>` : 
                                    `<span class="badge bg-secondary">No proof available</span>`}
                                </div>
                            </div>
                        </div>
                    `;
                });
                html += '</div>';
                
                container.html(html);
            })
            .catch(error => {
                console.error('Error fetching student attendance records:', error);
                $('#studentSeminars').html('<p>Unable to load attendance data.</p>');
            });
    }

    // Load extracurricular activities for a student
    function loadStudentExtracurricular(studentId) {
        fetch(`/Dashboard/GetStudentExtraCurricularRecords?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student extracurricular data (full response):", data);
                const container = $('#studentExtracurricular');
                
                if (!data || data.length === 0) {
                    container.html('<p>No extracurricular activities found.</p>');
                    return;
                }
                
                // Debug: inspect the first record's structure
                if (data.length > 0) {
                    console.log("First extracurricular record:", data[0]);
                    console.log("First record direct access to rank:", {
                        lowercase_rank: data[0].rank,
                        uppercase_rank: data[0].Rank,
                        raw_value: data[0]['rank']
                    });
                }
                
                let html = '<div class="seminars-list">'; // Using the same CSS class for consistent styling
                data.forEach(activity => {
                    // Log all properties of the activity object to see what's available
                    console.log("Activity object keys:", Object.keys(activity));
                    
                    // Determine rank display from the extracurricularactivities table's Rank column
                    // Check both database field naming conventions (lowercase and PascalCase)
                    let rankValue = 'N/A';
                    
                    // Try to access the rank field (which might be named differently in the response)
                    if (typeof activity.rank !== 'undefined' && activity.rank !== null) {
                        rankValue = activity.rank;
                        console.log(`Found rank (lowercase): ${rankValue} type: ${typeof rankValue}`);
                    }
                    else if (typeof activity.Rank !== 'undefined' && activity.Rank !== null) {
                        rankValue = activity.Rank;
                        console.log(`Found Rank (uppercase): ${rankValue} type: ${typeof rankValue}`);
                    }
                    // Try other possible variations of the field name
                    else if (typeof activity.activityRank !== 'undefined' && activity.activityRank !== null) rankValue = activity.activityRank;
                    else if (typeof activity.studentRank !== 'undefined' && activity.studentRank !== null) rankValue = activity.studentRank;
                    else if (typeof activity.position !== 'undefined' && activity.position !== null) rankValue = activity.position;
                    else if (typeof activity.placement !== 'undefined' && activity.placement !== null) rankValue = activity.placement;
                    
                    // Check if it's NULL in the database (will often come through as a string "NULL")
                    if (rankValue === "NULL" || rankValue === null) {
                        rankValue = "N/A";
                    }
                    
                    // Determine rank badge style based on text values
                    let rankBadgeStyle = '';
                    let rankBadgeClass = '';
                    
                    if (rankValue !== 'N/A') {
                        // Handle text-based ranks like "champion"
                        if (typeof rankValue === 'string') {
                            const lowerRank = rankValue.toLowerCase();
                            
                            if (lowerRank === 'champion') {
                                rankBadgeStyle = 'background-color: gold; color: black;';
                                rankBadgeClass = 'rank-badge-gold';
                            } 
                            else if (lowerRank === 'finalist' || lowerRank === 'runner-up' || lowerRank === 'runner up') {
                                rankBadgeStyle = 'background-color: silver; color: black;';
                                rankBadgeClass = 'rank-badge-silver';
                            }
                            else if (lowerRank === 'semi-finalist' || lowerRank === 'semifinalist') {
                                rankBadgeStyle = 'background-color: #cd7f32; color: white;';
                                rankBadgeClass = 'rank-badge-bronze';
                            }
                            else {
                                rankBadgeStyle = 'background-color: #6c757d; color: white;';
                                rankBadgeClass = 'rank-badge-other';
                            }
                        }
                        // Handle numeric ranks if they exist
                        else if (!isNaN(rankValue)) {
                            const num = parseInt(rankValue);
                            if (num === 1) {
                                rankBadgeStyle = 'background-color: gold; color: black;';
                                rankBadgeClass = 'rank-badge-gold';
                                rankValue = "1st";
                            } 
                            else if (num === 2) {
                                rankBadgeStyle = 'background-color: silver; color: black;';
                                rankBadgeClass = 'rank-badge-silver';
                                rankValue = "2nd";
                            } 
                            else if (num === 3) {
                                rankBadgeStyle = 'background-color: #cd7f32; color: white;';
                                rankBadgeClass = 'rank-badge-bronze';
                                rankValue = "3rd";
                            }
                            else {
                                rankBadgeStyle = 'background-color: #6c757d; color: white;';
                                rankBadgeClass = 'rank-badge-other';
                                rankValue = num + "th";
                            }
                        }
                    }
                    
                    // Create rank badge HTML if applicable
                    const rankBadgeHtml = rankValue !== 'N/A' 
                        ? `<span class="badge ${rankBadgeClass}" style="${rankBadgeStyle} margin-left: 5px; padding: 3px 6px; border-radius: 4px;">${rankValue}</span>` 
                        : '';
                    
                    html += `
                        <div class="seminar-item">
                            <div class="seminar-icon"><i class="fas fa-trophy"></i></div>
                            <div class="seminar-details">
                                <div class="seminar-name">${activity.activityName} ${rankBadgeHtml}</div>
                                <div class="seminar-desc">${activity.activityDescription || ''}</div>
                                <div class="seminar-info">
                                    <span class="seminar-date">Date: ${new Date(activity.activityDate).toLocaleDateString()}</span>
                                    <span class="seminar-teacher">Category: ${activity.activityCategory}</span>
                                    <span class="seminar-teacher">Verified by: ${activity.teacherName}</span>
                                    <span class="seminar-score">Score: ${activity.score}</span>
                                    <span class="seminar-rank" style="font-weight: bold; color: #333;">Rank: ${rankValue}</span>
                                </div>`;
                    
                    if (activity.hasProofImage) {
                        html += `
                            <div class="seminar-actions">
                                <button class="btn btn-sm btn-primary view-proof-btn" 
                                        onclick="openExtraCurricularProofModal(${activity.activityId}, '${activity.activityName}')">
                                    View Proof
                                </button>
                            </div>`;
                    }
                    
                    html += `
                            </div>
                        </div>`;
                });
                html += '</div>';
                
                container.html(html);
            })
            .catch(error => {
                console.error('Error fetching extracurricular activities:', error);
                $('#studentExtracurricular').html('<p>Error loading extracurricular activities. Please try again later.</p>');
            });
    }

    function loadStudentProfile(studentId) {
        // Reset content sections
        $('#studentCertificates').html('<p>Loading certificate data...</p>');
        $('#studentSeminars').html('<p>Loading seminar data...</p>');
        $('#studentExtracurricular').html('<p>Loading extracurricular activities...</p>');
        
        // Reset grade values to loading state
        $('#FirstYearGrade').text('Loading...');
        $('#SecondYearGrade').text('Loading...');
        $('#ThirdYearGrade').text('Loading...');
        $('#FourthYearGrade').text('Loading...');
        
        // Reset score breakdown values
        $('#academicGradesScore').text('-');
        $('#challengesScore').text('-');
        $('#masteryScore').text('-');
        $('#seminarsScore').text('-');
        $('#extracurricularScore').text('-');
        $('#totalScore').text('-');
        $('#challengeStatsContainer').hide();
        
        // Load score breakdown data
        loadStudentScoreBreakdown(studentId);
        
        // Fetch student profile details including grades
        fetch(`/Dashboard/GetStudentProfileForEmployer?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student profile data:", data);
                
                if (data.success && data.student) {
                    const student = data.student;
                    console.log("Student grades:", student.FirstYearGrade, student.SecondYearGrade, student.ThirdYearGrade, student.FourthYearGrade);
                    
                    // Debug information to check what's coming from the server
                    console.log("First Year Grade:", student.FirstYearGrade, typeof student.FirstYearGrade);
                    console.log("Second Year Grade:", student.SecondYearGrade, typeof student.SecondYearGrade);
                    console.log("Third Year Grade:", student.ThirdYearGrade, typeof student.ThirdYearGrade);
                    console.log("Fourth Year Grade:", student.FourthYearGrade, typeof student.FourthYearGrade);
                    
                    // Update academic grade values with explicit null/undefined checking
                    $('#FirstYearGrade').text(student.FirstYearGrade !== null && student.FirstYearGrade !== undefined ? student.FirstYearGrade + '%' : 'Not Available');
                    $('#SecondYearGrade').text(student.SecondYearGrade !== null && student.SecondYearGrade !== undefined ? student.SecondYearGrade + '%' : 'Not Available');
                    $('#ThirdYearGrade').text(student.ThirdYearGrade !== null && student.ThirdYearGrade !== undefined ? student.ThirdYearGrade + '%' : 'Not Available');
                    $('#FourthYearGrade').text(student.FourthYearGrade !== null && student.FourthYearGrade !== undefined ? student.FourthYearGrade + '%' : 'Not Available');
                } else {
                    // Handle case where student data isn't available
                    $('#FirstYearGrade').text('Not Available');
                    $('#SecondYearGrade').text('Not Available');
                    $('#ThirdYearGrade').text('Not Available');
                    $('#FourthYearGrade').text('Not Available');
                }
            })
            .catch(error => {
                console.error('Error fetching student profile data:', error);
                // Set default values in case of an error
                $('#FirstYearGrade').text('Not Available');
                $('#SecondYearGrade').text('Not Available');
                $('#ThirdYearGrade').text('Not Available');
                $('#FourthYearGrade').text('Not Available');
            });
        
        // Load all student data
        loadStudentCertificates(studentId);
        loadStudentSeminars(studentId);
        loadStudentExtracurricular(studentId);
    }
    
    // Function to load student score breakdown data
    function loadStudentScoreBreakdown(studentId) {
     // Show loading state
$('#academicGradesScore').text('Loading...');
$('#challengesScore').text('Loading...');
$('#masteryScore').text('Loading...');
$('#seminarsScore').text('Loading...');
$('#extracurricularScore').text('Loading...');
$('#totalScore').text('Loading...');

// Also set loading state for weight values
$('#academicGradesWeight').text('Loading...');
$('#challengesWeight').text('Loading...');
$('#masteryWeight').text('Loading...');
$('#seminarsWeight').text('Loading...');
$('#extracurricularWeight').text('Loading...');

// Fetch weights from the database to initialize the table
getScoreWeightsAndUpdateDisplay();

        fetch(`/Score/GetStudentScoreBreakdown?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    // If we get a 404, try getting score data from the student element instead
                    if (response.status === 404) {
                        console.warn('Student not found in score breakdown, using fallback data');
                        return { success: false, error: 'Student not found' };
                    }
                    throw new Error(`Network response was not ok: ${response.status}`);
                }
                return response.json();
            })
            .then(data => {
                console.log("Score breakdown data:", data);
                
                if (data.success) {
                    // Extract scores from the nested structure
                    const scores = data.scores || {};
                    
                    // Attempt to extract all score data regardless of structure format
                    let academicScore = 0;
                    let challengesScore = 0;
                    let masteryScore = 0;
                    let seminarsScore = 0;
                    let extracurricularScore = 0;
                    
                    // Try nested structure first
                    if (scores.academic && typeof scores.academic === 'object') {
                        // Get nested scores
                        academicScore = parseFloat(scores.academic.percentage || 0);
                        
                        if (scores.challenges) {
                            challengesScore = parseFloat(scores.challenges.percentage || 0);
                        }
                        
                        masteryScore = parseFloat(scores.mastery?.percentage || 0);
                        seminarsScore = parseFloat(scores.seminars?.percentage || 0);
                        extracurricularScore = parseFloat(scores.extracurricular?.percentage || 0);
                        
                        console.log("Score structure: nested object format", {
                            academicScore,
                            challengesScore,
                            masteryScore,
                            seminarsScore,
                            extracurricularScore
                        });
                    } 
                    // Try different formats in order of likelihood
                    else {
                        // Look for scores in data.details
                        const details = data.details || {};
                        
                        academicScore = parseFloat(details.academicScore || details.AcademicScore || 0);
                        challengesScore = parseFloat(details.challengesScore || details.ChallengesScore || 0);
                        masteryScore = parseFloat(details.masteryScore || details.MasteryScore || 0);
                        seminarsScore = parseFloat(details.seminarsScore || details.SeminarsScore || 0); 
                        extracurricularScore = parseFloat(details.extracurricularScore || details.ExtracurricularScore || 0);
                        
                        // If not found in details, try top-level properties
                        if (academicScore === 0) academicScore = parseFloat(data.academicScore || data.AcademicScore || 0);
                        if (challengesScore === 0) challengesScore = parseFloat(data.challengesScore || data.ChallengesScore || data.completedChallengesScore || 0);
                        if (masteryScore === 0) masteryScore = parseFloat(data.masteryScore || data.MasteryScore || 0);
                        if (seminarsScore === 0) seminarsScore = parseFloat(data.seminarsScore || data.SeminarsScore || 0);
                        if (extracurricularScore === 0) extracurricularScore = parseFloat(data.extracurricularScore || data.ExtracurricularScore || 0);
                        
                        console.log("Score structure: flat object format", {
                            academicScore,
                            challengesScore,
                            masteryScore,
                            seminarsScore,
                            extracurricularScore
                        });
                    }
                    
                    // Ensure all scores are valid numbers
                    academicScore = !isNaN(academicScore) ? academicScore : 0;
                    challengesScore = !isNaN(challengesScore) ? challengesScore : 0;
                    masteryScore = !isNaN(masteryScore) ? masteryScore : 0;
                    seminarsScore = !isNaN(seminarsScore) ? seminarsScore : 0;
                    extracurricularScore = !isNaN(extracurricularScore) ? extracurricularScore : 0;
                    
                    // Format the scores with up to 2 decimal places and percentage sign
                    $('#academicGradesScore').text(formatScore(academicScore));
                    
                    // Enhanced handling for challenge score
                    if (challengesScore !== null && challengesScore !== undefined && !isNaN(challengesScore)) {
                        $('#challengesScore').text(formatScore(challengesScore));
                        console.log(`Setting challenge score display to: ${formatScore(challengesScore)}`);
                    } else {
                        console.warn('Challenge score is invalid, using 0%');
                        $('#challengesScore').text('0.00%');
                    }
                    
                    $('#masteryScore').text(formatScore(masteryScore));
                    $('#seminarsScore').text(formatScore(seminarsScore));
                    $('#extracurricularScore').text(formatScore(extracurricularScore));
                    
                    // Update the weights from the API response
                    if (scores.academic && scores.academic.weight !== undefined) {
                        // New nested structure with weight values
                        $('#academicGradesWeight').text(formatScore(scores.academic.weight));
                        $('#challengesWeight').text(formatScore(scores.challenges.weight));
                        $('#masteryWeight').text(formatScore(scores.mastery.weight));
                        $('#seminarsWeight').text(formatScore(scores.seminars.weight));
                        $('#extracurricularWeight').text(formatScore(scores.extracurricular.weight));
                    } else if (data.weights) {
                        // Alternative structure with weights object
                        $('#academicGradesWeight').text(formatScore(data.weights.academic || 30));
                        $('#challengesWeight').text(formatScore(data.weights.challenges || 20));
                        $('#masteryWeight').text(formatScore(data.weights.mastery || 20));
                        $('#seminarsWeight').text(formatScore(data.weights.seminars || 10));
                        $('#extracurricularWeight').text(formatScore(data.weights.extracurricular || 20));
                    } else {
                        // Fallback to default weights
                        $('#academicGradesWeight').text('30.00%');
                        $('#challengesWeight').text('20.00%');
                        $('#masteryWeight').text('20.00%');
                        $('#seminarsWeight').text('10.00%');
                        $('#extracurricularWeight').text('20.00%');
                    }
                    
                    // Get current score from overall structure
                    let currentScore = 0;
                    
                    // Try different formats in order of likelihood
                    if (data.overall && data.overall.percentage !== undefined) {
                        currentScore = parseFloat(data.overall.percentage);
                    } else if (data.currentScore !== undefined) {
                        currentScore = parseFloat(data.currentScore);
                    } else if (data.totalScore !== undefined) {
                        currentScore = parseFloat(data.totalScore);
                    } else if (data.score !== undefined) {
                        currentScore = parseFloat(data.score);
                    } else {
                        // If no direct score is available, estimate from component scores
                        // Based on standard weights: 30% academic, 20% challenges, 20% mastery, 10% seminars, 20% extracurricular
                        currentScore = (academicScore * 0.3) + 
                                     (challengesScore * 0.2) + 
                                     (masteryScore * 0.2) + 
                                     (seminarsScore * 0.1) + 
                                     (extracurricularScore * 0.2);
                        console.log("Calculating total score from components:", currentScore);
                    }
                    
                    // Ensure score is a valid number
                    currentScore = !isNaN(currentScore) ? currentScore : 0;
                    
                    // Use the score from the API response
                    $('#totalScore').text(formatScore(currentScore));
                    
                    // Update the student's score in the header
                    const studentScoreElement = document.getElementById('studentScore');
                    if (studentScoreElement) {
                        studentScoreElement.textContent = `Score: ${formatScore(currentScore)}`;
                    }
                    
                    // Show challenge stats if available
                    if (data.completedChallenges !== undefined && data.totalAvailableChallenges !== undefined) {
                        if (data.totalAvailableChallenges > 0) {
                            $('#completedChallenges').text(data.completedChallenges);
                            $('#totalChallenges').text(data.totalAvailableChallenges);
                            $('#challengeStatsContainer').show();
                        } else {
                            $('#challengeStatsContainer').hide();
                        }
                    }

                    // Update badge color based on the current score
                    const badgeElement = document.getElementById('studentBadge');
                    if (badgeElement) {
                        let badgeColor = "warning";
                        if (currentScore >= 95) badgeColor = "platinum";
                        else if (currentScore >= 85) badgeColor = "gold";
                        else if (currentScore >= 75) badgeColor = "silver";
                        else if (currentScore >= 65) badgeColor = "bronze";
                        else if (currentScore >= 50) badgeColor = "rising-star";
                        
                        badgeElement.textContent = `Badge: ${badgeColor}`;
                        badgeElement.style.backgroundColor = 
                            badgeColor === 'platinum' ? '#b9f2ff' : 
                            badgeColor === 'gold' ? '#ffe34f' : 
                            badgeColor === 'silver' ? '#cfcccc' : 
                            badgeColor === 'bronze' ? '#f5b06c' : 
                            badgeColor === 'rising-star' ? '#98fb98' : 
                            '#ffcccb'; // warning
                        
                        // Set text color for better contrast
                        badgeElement.style.color = 
                            (badgeColor === 'platinum' || badgeColor === 'gold' || 
                             badgeColor === 'silver' || badgeColor === 'bronze' || 
                             badgeColor === 'rising-star') ? '#333' : '#333';
                    }
                    
                    // Update ranking text based on the current score
                    let rankingText = "";
                    if (currentScore >= 90) rankingText = "top 10%";
                    else if (currentScore >= 80) rankingText = "top 25%";
                    else if (currentScore >= 70) rankingText = "top 50%";
                    else rankingText = "below average";
                    
                    const rankingElement = document.getElementById('studentRanking');
                    if (rankingElement) {
                        rankingElement.textContent = rankingText;
                    }
                } else {
                    // Handle error or student not found case - use fallback data from student element
                    console.warn("Failed to load score breakdown:", data.error || "Unknown error");
                    
                    // Try to get the student element from the list
                    const studentElement = document.querySelector(`.student-item[data-student-id="${studentId}"]`);
                    if (studentElement) {
                        // Get scores from data attributes
                        const academicScore = parseFloat(studentElement.getAttribute('data-academic-score') || '0');
                        const masteryScore = parseFloat(studentElement.getAttribute('data-mastery-score') || '0');
                        const seminarsScore = parseFloat(studentElement.getAttribute('data-seminars-score') || '0');
                        const extracurricularScore = parseFloat(studentElement.getAttribute('data-extracurricular-score') || '0');
                        const totalScore = parseFloat(studentElement.getAttribute('data-score') || '0');
                        
                        // Display the scores we have
                        $('#academicGradesScore').text(formatScore(academicScore));
                        $('#masteryScore').text(formatScore(masteryScore));
                        $('#seminarsScore').text(formatScore(seminarsScore));
                        $('#extracurricularScore').text(formatScore(extracurricularScore));
                        $('#totalScore').text(formatScore(totalScore));
                        
                        // We might not have challenges score in the element, so display '0.00%'
                        $('#challengesScore').text('0.00%');
                        
                        // Set weights from database via API instead of hardcoded values
                        fetch('/Score/GetScoreWeights')
                            .then(response => response.ok ? response.json() : null)
                            .then(data => {
                                if (data && data.success) {
                                    $('#academicGradesWeight').text(formatScore(data.weights.AcademicGrades));
                                    $('#challengesWeight').text(formatScore(data.weights.CompletedChallenges));
                                    $('#masteryWeight').text(formatScore(data.weights.Mastery));
                                    $('#seminarsWeight').text(formatScore(data.weights.SeminarsWebinars));
                                    $('#extracurricularWeight').text(formatScore(data.weights.Extracurricular));
                                } else {
                                    // Fall back to defaults if API call fails
                                    $('#academicGradesWeight').text('30.00%');
                                    $('#challengesWeight').text('20.00%');
                                    $('#masteryWeight').text('20.00%');
                                    $('#seminarsWeight').text('10.00%');
                                    $('#extracurricularWeight').text('20.00%');
                                }
                            })
                            .catch(error => {
                                console.error('Error fetching score weights:', error);
                                // Fall back to defaults if API call fails
                                $('#academicGradesWeight').text('30.00%');
                                $('#challengesWeight').text('20.00%');
                                $('#masteryWeight').text('20.00%');
                                $('#seminarsWeight').text('10.00%');
                                $('#extracurricularWeight').text('20.00%');
                            });
                        
                        // Check if we can retrieve challenge score from the dashboard API as a fallback
                        fetch(`/Dashboard/GetStudentScore?studentId=${studentId}`)
                            .then(response => response.ok ? response.json() : null)
                            .then(scoreData => {
                                if (scoreData && scoreData.success && scoreData.challengesScore !== undefined) {
                                    console.log(`Retrieved challenge score from dashboard API: ${scoreData.challengesScore}`);
                                    const dashboardChallengeScore = parseFloat(scoreData.challengesScore);  // No multiplication
                                    $('#challengesScore').text(formatScore(dashboardChallengeScore));
                                }
                            })
                            .catch(err => console.error('Error fetching fallback challenge score:', err));
                        
                        // Update student header score
                        const studentScoreElement = document.getElementById('studentScore');
                        if (studentScoreElement) {
                            studentScoreElement.textContent = `Score: ${formatScore(totalScore)}`;
                        }
                    } else {
                        // No fallback data available
                        $('#academicGradesScore').text('N/A');
                        $('#challengesScore').text('N/A');
                        $('#masteryScore').text('N/A');
                        $('#seminarsScore').text('N/A');
                        $('#extracurricularScore').text('N/A');
                        $('#totalScore').text('N/A');
                        
                        // Set weights from database via API instead of hardcoded values
                        fetch('/Score/GetScoreWeights')
                            .then(response => response.ok ? response.json() : null)
                            .then(weightData => {
                                if (weightData && weightData.success) {
                                    $('#academicGradesWeight').text(formatScore(weightData.weights.AcademicGrades));
                                    $('#challengesWeight').text(formatScore(weightData.weights.CompletedChallenges));
                                    $('#masteryWeight').text(formatScore(weightData.weights.Mastery));
                                    $('#seminarsWeight').text(formatScore(weightData.weights.SeminarsWebinars));
                                    $('#extracurricularWeight').text(formatScore(weightData.weights.Extracurricular));
                                } else {
                                    // Fallback to default weights if API call fails
                                    $('#academicGradesWeight').text('30.00%');
                                    $('#challengesWeight').text('20.00%');
                                    $('#masteryWeight').text('20.00%');
                                    $('#seminarsWeight').text('10.00%');
                                    $('#extracurricularWeight').text('20.00%');
                                }
                            })
                            .catch(error => {
                                console.error('Error fetching score weights:', error);
                                // Fallback to default weights if API call fails
                                $('#academicGradesWeight').text('30.00%');
                                $('#challengesWeight').text('20.00%');
                                $('#masteryWeight').text('20.00%');
                                $('#seminarsWeight').text('10.00%');
                                $('#extracurricularWeight').text('20.00%');
                            });
                        
                        $('#challengeStatsContainer').hide();
                    }
                }
            })
            .catch(error => {
                console.error('Error fetching score breakdown:', error);
                
                // Try fallback from dashboard API
                fetch(`/Dashboard/GetStudentScore?studentId=${studentId}`)
                    .then(response => response.ok ? response.json() : Promise.reject('Fallback API failed'))
                    .then(data => {
                        if (data && data.success) {
                            console.log('Using fallback data from Dashboard API');
                            $('#academicGradesScore').text(formatScore(data.academicScore));  // No multiplication
                            $('#challengesScore').text(formatScore(data.challengesScore));    // No multiplication
                            $('#masteryScore').text(formatScore(data.masteryScore));          // No multiplication
                            $('#seminarsScore').text(formatScore(data.seminarsScore));        // No multiplication
                            $('#extracurricularScore').text(formatScore(data.extracurricularScore)); // No multiplication
                            $('#totalScore').text(formatScore(data.score));                   // No multiplication
                            
                            // Set default weights for fallback from Dashboard API
                            // Get weight values from the database
                            fetch('/Score/GetScoreWeights')
                                .then(response => response.ok ? response.json() : null)
                                .then(weightData => {
                                    if (weightData && weightData.success) {
                                        $('#academicGradesWeight').text(formatScore(weightData.weights.AcademicGrades));
                                        $('#challengesWeight').text(formatScore(weightData.weights.CompletedChallenges));
                                        $('#masteryWeight').text(formatScore(weightData.weights.Mastery));
                                        $('#seminarsWeight').text(formatScore(weightData.weights.SeminarsWebinars));
                                        $('#extracurricularWeight').text(formatScore(weightData.weights.Extracurricular));
                                    } else {
                                        // Fallback to default weights if API call fails
                                        $('#academicGradesWeight').text('30.00%');
                                        $('#challengesWeight').text('20.00%');
                                        $('#masteryWeight').text('20.00%');
                                        $('#seminarsWeight').text('10.00%');
                                        $('#extracurricularWeight').text('20.00%');
                                    }
                                })
                                .catch(error => {
                                    console.error('Error fetching score weights:', error);
                                    // Fallback to default weights if API call fails
                                    $('#academicGradesWeight').text('30.00%');
                                    $('#challengesWeight').text('20.00%');
                                    $('#masteryWeight').text('20.00%');
                                    $('#seminarsWeight').text('10.00%');
                                    $('#extracurricularWeight').text('20.00%');
                                });
                        } else {
                            // Display error state
                            $('#academicGradesScore').text('Error');
                            $('#challengesScore').text('Error');
                            $('#masteryScore').text('Error');
                            $('#seminarsScore').text('Error');
                            $('#extracurricularScore').text('Error');
                            $('#totalScore').text('Error');
                            
                            // Set default weights for error state
                            $('#academicGradesWeight').text('30.00%');
                            $('#challengesWeight').text('20.00%');
                            $('#masteryWeight').text('20.00%');
                            $('#seminarsWeight').text('10.00%');
                            $('#extracurricularWeight').text('20.00%');
                            
                            $('#challengeStatsContainer').hide();
                        }
                    })
                    .catch(fallbackError => {
                        console.error('Fallback also failed:', fallbackError);
                        // Display error state
                        $('#academicGradesScore').text('Error');
                        $('#challengesScore').text('Error');
                        $('#masteryScore').text('Error');
                        $('#seminarsScore').text('Error');
                        $('#extracurricularScore').text('Error');
                        $('#totalScore').text('Error');
                        
                        // Set default weights for error state
                        $('#academicGradesWeight').text('30.00%');
                        $('#challengesWeight').text('20.00%');
                        $('#masteryWeight').text('20.00%');
                        $('#seminarsWeight').text('10.00%');
                        $('#extracurricularWeight').text('20.00%');
                        
                        $('#challengeStatsContainer').hide();
                    });
            });
    }

    // Helper function to format score with percentage sign
    function formatScore(score) {
        if (score === null || score === undefined || isNaN(parseFloat(score))) {
            return '0.00%';
        }
        return parseFloat(score).toFixed(2) + '%';
    }

    // Add this function after the formatScore function
function getScoreWeightsAndUpdateDisplay() {
fetch('/Score/GetScoreWeights')
    .then(response => response.ok ? response.json() : null)
    .then(data => {
        if (data && data.success) {
            $('#academicGradesWeight').text(formatScore(data.weights.AcademicGrades));
            $('#challengesWeight').text(formatScore(data.weights.CompletedChallenges));
            $('#masteryWeight').text(formatScore(data.weights.Mastery));
            $('#seminarsWeight').text(formatScore(data.weights.SeminarsWebinars));
            $('#extracurricularWeight').text(formatScore(data.weights.Extracurricular));
        } else {
            // Fall back to defaults if API call fails
            $('#academicGradesWeight').text('30.00%');
            $('#challengesWeight').text('20.00%');
            $('#masteryWeight').text('20.00%');
            $('#seminarsWeight').text('10.00%');
            $('#extracurricularWeight').text('20.00%');
        }
    })
    .catch(error => {
        console.error('Error fetching score weights:', error);
        // Fall back to defaults if API call fails
        $('#academicGradesWeight').text('30.00%');
        $('#challengesWeight').text('20.00%');
        $('#masteryWeight').text('20.00%');
        $('#seminarsWeight').text('10.00%');
        $('#extracurricularWeight').text('20.00%');
    });
}


    // Function to load seminar data 
    function loadStudentSeminars(studentId) {
        fetch(`/Dashboard/GetStudentAttendanceRecords?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Student seminar data:", data);
                const container = $('#studentSeminars');
                
                if (!data || data.length === 0) {
                    container.html('<p>No seminar or webinar attendance records available.</p>');
                    return;
                }
                
                let html = '<div class="seminars-list">';
                data.forEach(record => {
                    html += `
                        <div class="seminar-item">
                            <div class="seminar-icon"><i class="fas fa-chalkboard-teacher"></i></div>
                            <div class="seminar-details">
                                <div class="seminar-name">${record.eventName}</div>
                                <div class="seminar-desc">${record.eventDescription || ''}</div>
                                <div class="seminar-info">
                                    <span class="seminar-date">Date: ${new Date(record.eventDate).toLocaleDateString()}</span>
                                    <span class="seminar-teacher">Verified by: ${record.teacherName || 'Teacher'}</span>
                                    <span class="seminar-score">Score: ${formatScore(record.score)}</span>
                                </div>
                                <div class="seminar-actions">
                                    ${record.hasProofImage ? 
                                    `<button class="btn btn-sm btn-primary view-proof-btn" 
                                            onclick="openAttendanceProofModal(${record.attendanceId}, '${record.eventName}')">
                                        View Proof
                                    </button>` : 
                                    `<span class="badge bg-secondary">No proof available</span>`}
                                </div>
                            </div>
                        </div>
                    `;
                });
                html += '</div>';
                
                container.html(html);
            })
            .catch(error => {
                console.error('Error fetching student seminar records:', error);
                $('#studentSeminars').html('<p>Unable to load seminar data.</p>');
            });
    }

    // Function to open the image modal
    function openImageModal(src, title) {
        document.getElementById('modalImage').src = src;
        document.getElementById('imageModalTitle').textContent = title;
        document.getElementById('imageModal').style.display = 'block';
    }
    
    // Function to open certificate modal
    function openCertificateModal(certificateId, title) {
        // Set loading state
        document.getElementById('modalImage').src = '/images/loading.gif';
        document.getElementById('imageModalTitle').textContent = title + ' Certificate';
        document.getElementById('imageModal').style.display = 'block';
        
        // Set fixed dimensions to match webinar proofs
        document.getElementById('modalImage').style.width = '800px';
        document.getElementById('modalImage').style.maxWidth = '95%';
        document.getElementById('modalImage').style.height = 'auto';
        document.getElementById('modalImage').style.objectFit = 'contain';
        document.getElementById('modalImage').style.margin = '0 auto';
        document.getElementById('modalImage').style.display = 'block';
        
        // Use timeout to ensure modal is visible before loading image
        setTimeout(() => {
            const src = `/ProgrammingTest/ViewCertificate/${certificateId}`;
            document.getElementById('modalImage').src = src;
        }, 100);
    }
    
    // Function to open attendance proof modal
    function openAttendanceProofModal(attendanceId, title) {
        // Set loading state
        document.getElementById('modalImage').src = '/images/loading.gif';
        document.getElementById('imageModalTitle').textContent = title + ' Attendance Proof';
        document.getElementById('imageModal').style.display = 'block';
        
        // Set fixed dimensions for consistency
        document.getElementById('modalImage').style.width = '800px';
        document.getElementById('modalImage').style.maxWidth = '95%'; 
        document.getElementById('modalImage').style.height = 'auto';
        document.getElementById('modalImage').style.objectFit = 'contain';
        document.getElementById('modalImage').style.margin = '0 auto';
        document.getElementById('modalImage').style.display = 'block';
        
        // Use timeout to ensure modal is visible before loading image
        setTimeout(() => {
            const src = `/Teacher/ViewAttendanceProof?id=${attendanceId}`;
            document.getElementById('modalImage').src = src;
        }, 100);
    }
    
    // Initialize modal event handlers when document is ready
    $(document).ready(function() {
        // Center the modal image container
        const modalContent = document.querySelector('.modal-content');
        if (modalContent) {
            modalContent.style.textAlign = 'center';
        }
        
        // When the user clicks on the close button, close the modal
        $('.close-image-modal').on('click', function() {
            document.getElementById('imageModal').style.display = 'none';
            document.getElementById('modalImage').src = '';
        });
        
        // When the user clicks anywhere outside of the modal content, close it
        $(window).on('click', function(event) {
            const modal = document.getElementById('imageModal');
            if (event.target == modal) {
                modal.style.display = 'none';
                document.getElementById('modalImage').src = '';
            }
        });
    });

    // Function to open extracurricular proof modal
    function openExtraCurricularProofModal(activityId, title) {
        // Set loading state
        document.getElementById('modalImage').src = '/images/loading.gif';
        document.getElementById('imageModalTitle').textContent = title + ' Activity Proof';
        document.getElementById('imageModal').style.display = 'block';
        
        // Set fixed dimensions to match webinar proofs
        document.getElementById('modalImage').style.width = '800px';
        document.getElementById('modalImage').style.maxWidth = '95%';
        document.getElementById('modalImage').style.height = 'auto';
        document.getElementById('modalImage').style.objectFit = 'contain';
        document.getElementById('modalImage').style.margin = '0 auto';
        document.getElementById('modalImage').style.display = 'block';
        
        // Use timeout to ensure modal is visible before loading image
        setTimeout(() => {
            const src = `/Teacher/ViewExtraCurricularProofImage?activityId=${activityId}`;
            document.getElementById('modalImage').src = src;
        }, 100);
    }

    // Function to filter students based on current active filter and toggle state
    function applyCurrentFilters() {
        try {
            const seniorToggle = document.getElementById('seniorToggle');
            if (!seniorToggle) {
                console.error('Senior toggle not found in applyCurrentFilters');
                return;
            }
            
            const showOnlySeniors = seniorToggle.checked;
            console.log('Applying filters - Show only seniors:', showOnlySeniors);
            
            // Get current active course filter
            let currentFilter = 'all';
            let currentCategory = '';
            const activeFilterBtn = document.querySelector('.filter-btn.active');
            
            if (activeFilterBtn) {
                const buttonText = activeFilterBtn.textContent.trim();
                
                if (buttonText === 'All Students') {
                    currentFilter = 'all';
                } else if (buttonText === 'Top Performers') {
                    currentFilter = 'top';
                } else if (buttonText === 'Top by Grades') {
                    currentCategory = 'grades';
                } else if (buttonText === 'Mastery') {
                    currentCategory = 'mastery';
                } else if (buttonText === 'Webinars/Seminars') {
                    currentCategory = 'webinars';
                } else if (buttonText === 'Extracurricular Activities') {
                    currentCategory = 'extracurricular';
                }
            }
            
            // If a category is selected, use the category filter instead
            if (currentCategory) {
                filterByCategory(currentCategory);
                return;
            }
            
            console.log('Current filter:', currentFilter);
            
            // Get all student items
            const studentItems = document.querySelectorAll('.student-item');
            console.log('Total student items:', studentItems.length);
            
            let visibleCount = 0;
            let seniorCount = 0;
            
            studentItems.forEach(function(item) {
                try {
                    // For "All Students", no course filter needed
                    let showBasedOnFilter = true;
                    
                    // For "Top Performers", we'll sort by score later
                    
                    // Apply senior filter if enabled
                    let showBasedOnYear = true;
                    if (showOnlySeniors) {
                        const gradeLevelStr = item.getAttribute('data-grade-level');
                        const gradeLevel = gradeLevelStr ? parseInt(gradeLevelStr) : 0;
                        showBasedOnYear = gradeLevel >= 4; // 4th year or graduated (5)
                        
                        if (showBasedOnYear) {
                            seniorCount++;
                        }
                    }
                    
                    // Show item only if it passes both filters
                    const shouldDisplay = (showBasedOnFilter && showBasedOnYear);
                    
                    // Check if the element has a style property
                    if (item && typeof item.style !== 'undefined') {
                        item.style.display = shouldDisplay ? 'flex' : 'none';
                        
                        if (shouldDisplay) {
                            visibleCount++;
                        }
                    }
                } catch (itemError) {
                    console.error('Error processing student item:', itemError);
                }
            });
            
            console.log(`Filter applied: ${visibleCount} students visible (${seniorCount} seniors) out of ${studentItems.length}`);
            
            // If we're showing top performers, sort them
            if (currentFilter === 'top') {
                sortStudentsByScore();
            }
        } catch (error) {
            console.error('Error in applyCurrentFilters:', error);
        }
    }

    // Modify existing filter functions to work with the toggle
    function filterStudents(course) {
        // Update active button
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.textContent.includes(course === 'all' ? 'All Students' : course)) {
                btn.classList.add('active');
            }
        });

        // Apply all filters
        applyCurrentFilters();
    }

    function filterTopPerformers() {
        // Update active button
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.textContent.includes('Top Performers')) {
                btn.classList.add('active');
            }
        });
        
        // Apply all filters
        applyCurrentFilters();
    }

    // Debug function to check all students and their grade levels
    function debugStudentFilters() {
        console.log('--- DEBUG: STUDENT FILTERS ---');
        
        // Check toggle state
        const seniorToggle = document.getElementById('seniorToggle');
        if (seniorToggle) {
            console.log('Toggle exists, checked:', seniorToggle.checked);
        } else {
            console.log('Toggle does not exist!');
        }
        
        // Check student items
        const studentItems = document.querySelectorAll('.student-item');
        console.log('Found', studentItems.length, 'student items');
        
        // Display grade levels for all students
        const gradeLevels = [];
        studentItems.forEach(item => {
            const name = item.querySelector('h3') ? item.querySelector('h3').textContent : 'Unknown';
            const gradeLevel = item.getAttribute('data-grade-level');
            gradeLevels.push({ name, gradeLevel: parseInt(gradeLevel || '0') });
            
            console.log(`Student: ${name}, Grade Level: ${gradeLevel}`);
        });
        
        // Count by grade level
        const counts = {};
        gradeLevels.forEach(student => {
            counts[student.gradeLevel] = (counts[student.gradeLevel] || 0) + 1;
        });
        
        console.log('Students by grade level:', counts);
        
        // Force refresh of filters
        applyCurrentFilters();
    }

    // Function to filter students by category
    function filterByCategory(category) {
        debugLog(`Filtering by category: ${category}`);
        
        // Update active button
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.classList.remove('active');
        });
        document.querySelector(`.filter-btn[data-filter="${category}"]`).classList.add('active');
        
        const studentContainer = document.getElementById('studentContainer');
        if (!studentContainer) {
            console.error("Student container not found");
            showNotification("Error", "Could not find student container");
            return;
        }
        
        // Save all student elements to an array before clearing the container
        // This ensures we have access to all students regardless of current display
        const allStudents = Array.from(document.querySelectorAll('.student-item'));
        debugLog(`Found ${allStudents.length} total student elements`);
        
        if (allStudents.length === 0) {
            studentContainer.innerHTML = '<p>No students found.</p>';
            return;
        }
        
        // Ensure all students have required data attributes
        allStudents.forEach(student => ensureStudentDataAttributes(student));
        
        // Reset any existing rank badges and styling on all students
        allStudents.forEach(student => {
            // Remove any existing rank badge
            const rankBadge = student.querySelector('.rank-badge');
            if (rankBadge) {
                rankBadge.remove();
            }
            // Reset styling
            student.style.borderLeft = '';
            student.style.boxShadow = '';
            student.style.backgroundColor = '';
        });
        
        // Clear the container completely to remove any previous students
        studentContainer.innerHTML = '';
        
        // Apply senior filter if checked
        let filteredStudents = allStudents;
        const seniorToggle = document.getElementById('seniorToggle');
        if (seniorToggle && seniorToggle.checked) {
            debugLog("Senior filter is active");
            filteredStudents = allStudents.filter(student => {
                const gradeLevel = parseInt(student.getAttribute('data-grade-level') || '1');
                return gradeLevel >= 4 || gradeLevel === 5; // 4th year or graduated
            });
            
            if (filteredStudents.length === 0) {
                studentContainer.innerHTML = '<p>No senior students found.</p>';
                return;
            }
        }
        
        // Apply category filter
        let eligibleStudents = filteredStudents;
        if (category !== 'all') {
            debugLog(`Applying category filter: ${category}`);
            eligibleStudents = filteredStudents.filter(student => {
                // Get score for this category
                let score = 0;
                switch(category) {
                    case 'top':
                        // For top performers, use overall score
                        score = parseFloat(student.getAttribute('data-score') || '0');
                        debugLog(`Student ${student.getAttribute('data-student-id')} - top score: ${score}`);
                        return score >= 50; // Only show students with decent scores
                        
                    case 'grades':
                        // Use academic grades score ONLY for this category
                        score = parseFloat(student.getAttribute('data-academic-score') || '0');
                        debugLog(`Student ${student.getAttribute('data-student-id')} - grades score: ${score}`);
                        return score > 0; // Only show students with grades
                        
                    case 'mastery':
                        // Use mastery score ONLY for this category
                        score = parseFloat(student.getAttribute('data-mastery-score') || '0');
                        debugLog(`Student ${student.getAttribute('data-student-id')} - mastery score: ${score}`);
                        return score > 0; // Only show students with mastery scores
                        
                    case 'webinars':
                        // Use seminars/webinars score ONLY for this category
                        score = parseFloat(student.getAttribute('data-seminars-score') || '0');
                        debugLog(`Student ${student.getAttribute('data-student-id')} - webinars score: ${score}`);
                        return score > 0; // Only show students with seminars scores
                        
                    case 'extracurricular':
                        // Use extracurricular score ONLY for this category
                        score = parseFloat(student.getAttribute('data-extracurricular-score') || '0');
                        debugLog(`Student ${student.getAttribute('data-student-id')} - extracurricular score: ${score}`);
                        return score > 0; // Only show students with extracurricular scores
                        
                    default:
                        return true; // All students pass by default
                }
            });
        }
        
        debugLog(`After filtering, found ${eligibleStudents.length} eligible students`);
        
        if (eligibleStudents.length === 0) {
            studentContainer.innerHTML = `<p>No students found for the "${category}" category.</p>`;
            debugLog(`No students matching category ${category}`);
            return;
        }
        
        // Sort and display students
        if (category === 'all') {
            // For 'all' category, we sort by the sum of all relevant scores except CompletedChallengesScore
            eligibleStudents.sort((a, b) => {
                // Get individual category scores
                const academicA = parseFloat(a.getAttribute('data-academic-score') || '0');
                const masteryA = parseFloat(a.getAttribute('data-mastery-score') || '0');
                const seminarsA = parseFloat(a.getAttribute('data-seminars-score') || '0');
                const extracurricularA = parseFloat(a.getAttribute('data-extracurricular-score') || '0');
                
                const academicB = parseFloat(b.getAttribute('data-academic-score') || '0');
                const masteryB = parseFloat(b.getAttribute('data-mastery-score') || '0');
                const seminarsB = parseFloat(b.getAttribute('data-seminars-score') || '0');
                const extracurricularB = parseFloat(b.getAttribute('data-extracurricular-score') || '0');
                
                // Calculate total (excluding CompletedChallengesScore)
                const totalA = academicA + masteryA + seminarsA + extracurricularA;
                const totalB = academicB + masteryB + seminarsB + extracurricularB;
                
                return totalB - totalA; // Sort descending (highest first)
            });
            
            // Re-append students in sorted order
            eligibleStudents.forEach((item, index) => {
                // Make sure the item is visible
                item.style.display = 'flex';
                studentContainer.appendChild(item);
                
                // Highlight top performers
                if (index < 3) {
                    highlightTopPerformer(item, index, 'all');
                }
            });
            
            debugLog(`Displayed all ${eligibleStudents.length} students sorted by overall score (excluding completed challenges)`);
        } else {
            // For specific categories, use category-specific sorting
            sortByCategoryScore(category, eligibleStudents, studentContainer);
        }
        
        // Force browser to repaint (can help with visual glitches)
        setTimeout(() => {
            studentContainer.style.opacity = '0.99';
            setTimeout(() => {
                studentContainer.style.opacity = '1';
            }, 50);
        }, 0);
        
        debugLog(`Completed filtering for category: ${category}`);
    }
    
    // Function to fetch and sort by webinar count
    function fetchAndSortByWebinarCount(visibleStudents, container) {
        debugLog(`Fetching webinar counts for ${visibleStudents.length} students`);
        
        if (visibleStudents.length === 0) {
            debugLog('No students to fetch webinar counts for');
            return;
        }
        
        // Show loading indicator
        const loadingIndicator = document.createElement('div');
        loadingIndicator.textContent = 'Loading seminar data...';
        loadingIndicator.style.position = 'fixed';
        loadingIndicator.style.bottom = '20px';
        loadingIndicator.style.left = '20px';
        loadingIndicator.style.backgroundColor = '#3498db';
        loadingIndicator.style.color = 'white';
        loadingIndicator.style.padding = '10px';
        loadingIndicator.style.borderRadius = '5px';
        loadingIndicator.style.zIndex = '1000';
        document.body.appendChild(loadingIndicator);
        
        // Seminar count cache
        const seminarCounts = new Map();
        const promises = [];
        
        // Fetch seminar counts for all visible students
        visibleStudents.forEach(item => {
            const studentId = item.getAttribute('data-student-id');
            if (!studentId) {
                debugLog(`Student item missing ID attribute`);
                return;
            }
            
            debugLog(`Fetching seminars for student ${studentId}`);
            
            const promise = fetch(`/Dashboard/GetStudentAttendanceRecords?studentId=${studentId}`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error(`Server returned ${response.status}`);
                    }
                    return response.json();
                })
                .then(data => {
                    // Store count of seminars
                    const count = Array.isArray(data) ? data.length : 0;
                    seminarCounts.set(studentId, count);
                    debugLog(`Student ${studentId} has ${count} webinars/seminars`);
                    
                    // Add visual count indicator
                    const countBadge = document.createElement('div');
                    countBadge.className = 'seminar-count-badge';
                    countBadge.textContent = count;
                    countBadge.style.position = 'absolute';
                    countBadge.style.top = '5px';
                    countBadge.style.right = '5px';
                    countBadge.style.backgroundColor = '#2ecc71';
                    countBadge.style.color = 'white';
                    countBadge.style.width = '24px';
                    countBadge.style.height = '24px';
                    countBadge.style.borderRadius = '50%';
                    countBadge.style.display = 'flex';
                    countBadge.style.alignItems = 'center';
                    countBadge.style.justifyContent = 'center';
                    countBadge.style.fontWeight = 'bold';
                    
                    // Remove any existing count badge
                    const existingBadge = item.querySelector('.seminar-count-badge');
                    if (existingBadge) {
                        existingBadge.remove();
                    }
                    
                    item.style.position = 'relative';
                    item.appendChild(countBadge);
                })
                .catch(error => {
                    debugLog(`Error fetching seminars for student ${studentId}: ${error.message}`);
                    console.error(`Error fetching seminars for student ${studentId}:`, error);
                    seminarCounts.set(studentId, 0);
                });
            
            promises.push(promise);
        });
        
        // After all data is loaded, sort and display
        Promise.all(promises)
            .then(() => {
                debugLog('All webinar data fetched, sorting students');
                
                // Sort students by seminar count (highest first)
                visibleStudents.sort((a, b) => {
                    const idA = a.getAttribute('data-student-id');
                    const idB = b.getAttribute('data-student-id');
                    return (seminarCounts.get(idB) || 0) - (seminarCounts.get(idA) || 0);
                });
                
                // Clear the container first
                while (container.firstChild) {
                    container.removeChild(container.firstChild);
                }
                
                // Re-append in sorted order
                visibleStudents.forEach((item, index) => {
                    // Make sure item is visible
                    item.style.display = 'flex';
                    container.appendChild(item);
                    
                    // Only highlight top 3
                    if (index < 3) {
                        // Highlight top performers
                        highlightTopPerformer(item, index, 'webinars');
                    }
                });
                
                debugLog('Students sorted and displayed by webinar count');
                
                // Remove loading indicator
                document.body.removeChild(loadingIndicator);
            })
            .catch(error => {
                debugLog(`ERROR in fetchAndSortByWebinarCount: ${error.message}`);
                console.error('Error in fetchAndSortByWebinarCount:', error);
                
                // Remove loading indicator
                document.body.removeChild(loadingIndicator);
                
                // Show error message to user
                alert('There was an error loading webinar data. Please try again.');
            });
    }
    
    // Function to sort by category-specific criteria
    function sortByCategoryScore(category, students, container) {
        debugLog(`Sorting ${students.length} students by category ${category}`);
        
        // Log original order (first few students)
        if (students.length > 0) {
            debugLog(`PRE-SORT - Top 3 students (original order):`);
            for (let i = 0; i < Math.min(3, students.length); i++) {
                const student = students[i];
                const id = student.getAttribute('data-student-id');
                const name = student.querySelector('h3')?.textContent || 'Unknown';
                let categoryScore = 0;
                
                switch(category) {
                    case 'grades': 
                        categoryScore = student.getAttribute('data-academic-score'); 
                        break;
                    case 'mastery': 
                        categoryScore = student.getAttribute('data-mastery-score'); 
                        break;
                    case 'webinars': 
                        categoryScore = student.getAttribute('data-seminars-score'); 
                        break;
                    case 'extracurricular': 
                        categoryScore = student.getAttribute('data-extracurricular-score'); 
                        break;
                    default: 
                        categoryScore = student.getAttribute('data-score');
                }
                
                debugLog(`${i+1}. ${name} (ID: ${id}) - ${category} score: ${categoryScore}`);
            }
        }
        
        // Sort based on category - using specific database columns
        students.sort((a, b) => {
            let scoreA, scoreB;
            
            switch(category) {
                case 'grades':
                    // Sort by AcademicGradesScore
                    scoreA = parseFloat(a.getAttribute('data-academic-score') || '0');
                    scoreB = parseFloat(b.getAttribute('data-academic-score') || '0');
                    break;
                    
                case 'mastery':
                    // Sort by MasteryScore
                    scoreA = parseFloat(a.getAttribute('data-mastery-score') || '0');
                    scoreB = parseFloat(b.getAttribute('data-mastery-score') || '0');
                    break;
                    
                case 'webinars':
                    // Sort by SeminarsWebinarsScore ONLY
                    scoreA = parseFloat(a.getAttribute('data-seminars-score') || '0');
                    scoreB = parseFloat(b.getAttribute('data-seminars-score') || '0');
                    break;
                    
                case 'extracurricular':
                    // Sort by ExtracurricularScore ONLY
                    scoreA = parseFloat(a.getAttribute('data-extracurricular-score') || '0');
                    scoreB = parseFloat(b.getAttribute('data-extracurricular-score') || '0');
                    break;
                    
                case 'top':
                    // For top category, we calculate total without CompletedChallengesScore
                    // Get individual scores
                    const academicA = parseFloat(a.getAttribute('data-academic-score') || '0');
                    const masteryA = parseFloat(a.getAttribute('data-mastery-score') || '0');
                    const seminarsA = parseFloat(a.getAttribute('data-seminars-score') || '0');
                    const extracurricularA = parseFloat(a.getAttribute('data-extracurricular-score') || '0');
                    
                    const academicB = parseFloat(b.getAttribute('data-academic-score') || '0');
                    const masteryB = parseFloat(b.getAttribute('data-mastery-score') || '0');
                    const seminarsB = parseFloat(b.getAttribute('data-seminars-score') || '0');
                    const extracurricularB = parseFloat(b.getAttribute('data-extracurricular-score') || '0');
                    
                    // Calculate sum (excluding CompletedChallengesScore)
                    scoreA = academicA + masteryA + seminarsA + extracurricularA;
                    scoreB = academicB + masteryB + seminarsB + extracurricularB;
                    break;
                    
                default:
                    // Default to overall score
                    scoreA = parseFloat(a.getAttribute('data-score') || '0');
                    scoreB = parseFloat(b.getAttribute('data-score') || '0');
            }
            
            // Sort descending (highest first)
            return scoreB - scoreA;
        });
        
        // Log sorted order (top students)
        if (students.length > 0) {
            debugLog(`POST-SORT - Top 3 students sorted by ${category}:`);
            for (let i = 0; i < Math.min(3, students.length); i++) {
                const student = students[i];
                const id = student.getAttribute('data-student-id');
                const name = student.querySelector('h3')?.textContent || 'Unknown';
                let categoryScore = 0;
                
                switch(category) {
                    case 'grades': 
                        categoryScore = student.getAttribute('data-academic-score'); 
                        break;
                    case 'mastery': 
                        categoryScore = student.getAttribute('data-mastery-score'); 
                        break;
                    case 'webinars': 
                        categoryScore = student.getAttribute('data-seminars-score'); 
                        break;
                    case 'extracurricular': 
                        categoryScore = student.getAttribute('data-extracurricular-score'); 
                        break;
                    default: 
                        categoryScore = student.getAttribute('data-score');
                }
                
                debugLog(`${i+1}. ${name} (ID: ${id}) - ${category} score: ${categoryScore}`);
            }
        }
        
        debugLog(`Sorted students, clearing container and displaying`);
        
        // Clear the container first (should already be cleared but just to be safe)
        while (container && container.firstChild) {
            container.removeChild(container.firstChild);
        }
        
        // Re-append students in sorted order
        students.forEach((item, index) => {
            // Make sure the item is visible
            item.style.display = 'flex';
            if (container) container.appendChild(item);
            
            // Only apply special highlighting to top 3
            if (index < 3) {
                // Highlight top performers
                highlightTopPerformer(item, index, category);
            }
        });
        
        debugLog(`Category sorting complete, displayed ${students.length} students`);
    }
    
    // Helper function to count keyword occurrences
    function countKeywordOccurrences(text, keywords) {
        const lowerText = text.toLowerCase();
        let count = 0;
        
        keywords.forEach(keyword => {
            // Count all instances of the keyword
            const regex = new RegExp(keyword, 'gi');
            const matches = lowerText.match(regex);
            if (matches) {
                count += matches.length;
            }
        });
        
        return count;
    }
    
    // Function to highlight top performers
    function highlightTopPerformer(item, index, category) {
        // Reset any existing highlight
        item.style.borderLeft = '';
        item.style.boxShadow = '';
        
        // Remove any existing rank badge
        const rankBadge = item.querySelector('.rank-badge');
        if (rankBadge) {
            rankBadge.remove();
        }
        
        // Only add visual indicator for top students (index 0 is the top student)
        if (index < 3) {
            let color;
            switch(category) {
                case 'grades': color = '#3498db'; break; // Blue
                case 'mastery': color = '#9b59b6'; break; // Purple
                case 'webinars': color = '#2ecc71'; break; // Green
                case 'extracurricular': color = '#f39c12'; break; // Orange
                default: color = '#e74c3c'; // Red
            }
            
            // Add left border to indicate rank, thicker for first place
            if (index === 0) {
                // First place gets a thicker border and more prominent shadow
                item.style.borderLeft = `6px solid ${color}`;
                item.style.boxShadow = '0 3px 15px rgba(0,0,0,0.15)';
                item.style.backgroundColor = `${color}10`; // Very light tint of the category color
            } else {
                // Second and third places get thinner borders
                item.style.borderLeft = `4px solid ${color}`;
                item.style.boxShadow = '0 2px 10px rgba(0,0,0,0.1)';
            }
        }
    }

    // Function to load score breakdown data
    function loadScoreBreakdown(studentId) {
        fetch(`/Score/GetStudentScoreBreakdown?studentId=${studentId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                console.log("Score breakdown data:", data);
                
                if (data.success) {
                    // Get total score from the proper structure
                    let totalScore = 0;
                    
                    // Check if we have the new nested structure with 'overall' property
                    if (data.overall && typeof data.overall === 'object') {
                        totalScore = parseFloat(data.overall.percentage) || 0;
                    } else {
                        totalScore = parseFloat(data.totalScore || data.currentScore) || 0;
                    }
                    
                    document.getElementById('studentScore').textContent = `Score: ${totalScore.toFixed(2)}`;
                    
                    // Update badge color based on the total score
                    const badgeElement = document.getElementById('studentBadge');
                    if (badgeElement) {
                        let badgeColor = "green";
                        if (totalScore >= 95) badgeColor = "platinum";
                        else if (totalScore >= 85) badgeColor = "gold";
                        else if (totalScore >= 75) badgeColor = "silver";
                        else if (totalScore >= 65) badgeColor = "bronze";
                        else if (totalScore >= 50) badgeColor = "rising-star";
                        else badgeColor = "warning";
                        
                        badgeElement.textContent = `Badge: ${badgeColor}`;
                        badgeElement.style.backgroundColor = badgeColor.toLowerCase();
                        badgeElement.style.color = getContrastTextColor(badgeColor.toLowerCase());
                    }
                    
                    // Determine ranking based on score
                    let rankingText = "";
                    if (totalScore >= 90) rankingText = "top 10%";
                    else if (totalScore >= 80) rankingText = "top 25%";
                    else if (totalScore >= 70) rankingText = "top 50%";
                    else rankingText = "below average";
                    document.getElementById('studentRanking').textContent = rankingText;
                }
            })
            .catch(error => {
                console.error('Error fetching score breakdown:', error);
            });
    }

    // Function to create student item HTML
    function createStudentItemHTML(student) {
        // Determine the badge style based on the badge color
        let badgeStyle = getBadgeClass(student.badgeColor);
        
        // Prepare achievements text (limit to first 3)
        let achievementsHTML = '';
        if (student.achievements) {
            const achievementsList = student.achievements.split(/[,|]+/).filter(a => a.trim() !== "");
            const limitedAchievements = achievementsList.slice(0, 3).map(a => a.trim());
            
            if (limitedAchievements.length > 0) {
                achievementsHTML = `<p class="student-achievements">
                    <i class="fas fa-award" style="color: gold;"></i> ${limitedAchievements.join(', ')}
                </p>`;
            }
        }
        
        // Get score value - this will be updated later for displayed students
        let scoreText = student.score !== null && student.score !== undefined ? parseFloat(student.score).toFixed(2) : "0.00";
        
        // Determine which pulse class to use based on score
        let pulseClass = "";
        const score = parseFloat(student.score || 0);
        if (score >= 95) pulseClass = "student-pulse-platinum";
        else if (score >= 85) pulseClass = "student-pulse-gold";
        else if (score >= 75) pulseClass = "student-pulse-silver";
        else if (score >= 65) pulseClass = "student-pulse-bronze";
        else if (score >= 50) pulseClass = "student-pulse-rising";
        else pulseClass = "student-pulse-needs";
        
        // Create HTML template
        return `
            <div class="student-item ${pulseClass}" data-student-id="${student.idNumber}" data-student-name="${student.fullName}" data-score="${score}">
                <div class="student-info">
                    <h3>${student.fullName}</h3>
                    <p>${student.course} - ${student.section}</p>
                    ${achievementsHTML}
                    <div class="student-score" data-student-id="${student.idNumber}">
                        ${scoreText} <span class="badge ${badgeStyle}">${student.badgeColor}</span>
                    </div>
                </div>
                <div class="student-actions">
                    <button class="action-btn view-profile-btn" onclick="showStudentProfile('${student.idNumber}', '${student.fullName}')">
                        <i class="fas fa-user"></i>
                    </button>
                    <button class="action-btn chat-btn" onclick="openChat('${student.idNumber}', '${student.fullName}')">
                        <i class="fas fa-comment-alt"></i>
                    </button>
                </div>
            </div>
        `;
    }
    
    // Function to update scores in the student list
    function updateStudentScoresInList() {
        console.log("Updating student scores in list...");
        // Get all visible student items
        const visibleStudentItems = document.querySelectorAll('.student-item:not([style*="display: none"])');
        
        // Update scores for each visible student
        visibleStudentItems.forEach(item => {
            const studentId = item.getAttribute('data-student-id');
            
            // Ensure grade level is at least 1
            const gradeLevelAttr = item.getAttribute('data-grade-level');
            const gradeLevel = (gradeLevelAttr && parseInt(gradeLevelAttr) > 0) ? parseInt(gradeLevelAttr) : 1;
            
            // Update grade level display if needed
            const yearElement = item.querySelector('.student-year');
            if (yearElement && (yearElement.textContent.includes('Year 0') || yearElement.textContent.includes('Year: Unknown'))) {
                yearElement.textContent = yearElement.textContent.replace(/Year 0|Year: Unknown/, 'Year 1');
            }
            
            // Set minimum grade level to 1
            if (!gradeLevelAttr || parseInt(gradeLevelAttr) <= 0) {
                item.setAttribute('data-grade-level', '1');
            }
            
            if (studentId) {
                console.log(`Fetching score for student: ${studentId}`);
                fetch(`/Score/GetStudentScoreBreakdown?studentId=${studentId}`)
                    .then(response => {
                        if (!response.ok) {
                            throw new Error('Network response was not ok');
                        }
                        return response.json();
                    })
                    .then(data => {
                        console.log(`Score data for ${studentId}:`, data);
                        if (data.success) {
                            // Get the current score directly from the response
                            const currentScore = parseFloat(data.currentScore) || 0;
                            const badgeColor = data.badgeColor || "warning";
                            
                            console.log(`Total score for ${studentId}: ${currentScore}`);
                            
                            // Update the score display
                            const scoreElement = item.querySelector(`.student-score[data-student-id="${studentId}"]`);
                            if (scoreElement) {
                                const badgeClass = getBadgeClass(badgeColor);
                                
                                // Format score with 2 decimal places for display
                                const formattedScore = currentScore.toFixed(2);
                                
                                // Update score text and badge
                                scoreElement.innerHTML = `
                                    ${formattedScore} <span class="badge ${badgeClass}">${badgeColor}</span>
                                `;
                                
                                // Also update the student item's data-score attribute
                                item.setAttribute('data-score', currentScore);
                                
                                // Update the student item's pulse class
                                const pulseClasses = ['student-pulse-platinum', 'student-pulse-gold', 
                                                     'student-pulse-silver', 'student-pulse-bronze', 
                                                     'student-pulse-rising', 'student-pulse-needs'];
                                
                                pulseClasses.forEach(cls => {
                                    item.classList.remove(cls);
                                });
                                
                                let newPulseClass = "";
                                if (currentScore >= 95) newPulseClass = "student-pulse-platinum";
                                else if (currentScore >= 85) newPulseClass = "student-pulse-gold";
                                else if (currentScore >= 75) newPulseClass = "student-pulse-silver";
                                else if (currentScore >= 65) newPulseClass = "student-pulse-bronze";
                                else if (currentScore >= 50) newPulseClass = "student-pulse-rising";
                                else newPulseClass = "student-pulse-needs";
                                
                                item.classList.add(newPulseClass);
                            }
                        }
                    })
                    .catch(error => {
                        console.error(`Error updating score for student ${studentId}:`, error);
                    });
            }
        });
        
        // After updating scores, re-sort the students
        setTimeout(sortStudentsByScore, 2000);
    }

    // When document is ready, set up event handlers and load initial data
    $(document).ready(function() {
        // Initialize filter buttons - REMOVED DUPLICATE HANDLER
        
        // Ensure all profile pictures have error handling
        ensureProfileImageErrorHandling();
        
        // Load initial data and update scores
        loadInitialData();
        
        // Ensure pulse animations are applied
        setTimeout(ensurePulseAnimations, 1000);
    });
    
    // Function to ensure all profile images have error handling
    function ensureProfileImageErrorHandling() {
        // Get all profile image elements
        const profileImages = document.querySelectorAll('img[id*="Profile"], img[id*="Avatar"], .profile-picture img');
        
        // Add error handler to each image
        profileImages.forEach(img => {
            img.onerror = function() {
                this.src = '/images/blank.jpg';
                // Remove onerror to prevent infinite loops if blank.jpg is also missing
                this.onerror = null;
            };
            
            // If the image is already broken, set it to blank.jpg
            if (!img.complete || img.naturalHeight === 0) {
                img.src = '/images/blank.jpg';
            }
        });
        
        // Add a global handler for any image with src containing "GetProfilePicture"
        document.addEventListener('error', function(event) {
            const target = event.target;
            if (target.tagName === 'IMG' && 
                (target.src.includes('GetProfilePicture') || 
                 target.src.includes('profiles'))) {
                target.src = '/images/blank.jpg';
            }
        }, true);
    }

    // Helper function to get badge class based on badge color
    function getBadgeClass(badgeColor) {
        if (!badgeColor || badgeColor.toLowerCase() === 'white') return 'badge-warning';
        
        switch(badgeColor.toLowerCase()) {
            case 'platinum': return 'badge-platinum';
            case 'gold': return 'badge-gold';
            case 'silver': return 'badge-silver';
            case 'bronze': return 'badge-bronze';
            case 'rising-star': return 'badge-rising-star';
            default: return 'badge-warning';
        }
    }
    
    // Function to load initial data
    function loadInitialData() {
        // Check if we already have students rendered from server-side
        const containerElement = document.getElementById('studentContainer');
        if (containerElement && containerElement.querySelectorAll('.student-item').length > 0) {
            console.log("Using server-side rendered student list");
            
            // Apply pulse classes to all existing student items based on their score
            const studentItems = containerElement.querySelectorAll('.student-item');
            studentItems.forEach(item => {
                const score = parseFloat(item.getAttribute('data-score') || '0');
                // Remove any existing pulse classes
                item.classList.remove(
                    'student-pulse-platinum', 
                    'student-pulse-gold', 
                    'student-pulse-silver', 
                    'student-pulse-bronze', 
                    'student-pulse-rising', 
                    'student-pulse-needs'
                );
                
                // Add appropriate pulse class based on score
                if (score >= 95) item.classList.add('student-pulse-platinum');
                else if (score >= 85) item.classList.add('student-pulse-gold');
                else if (score >= 75) item.classList.add('student-pulse-silver');
                else if (score >= 65) item.classList.add('student-pulse-bronze');
                else if (score >= 50) item.classList.add('student-pulse-rising');
                else item.classList.add('student-pulse-needs');
            });
            
            // Students already rendered from server-side, just update scores
            setTimeout(updateStudentScoresInList, 500);
            setTimeout(sortStudentsByScore, 1000);
            return;
        }
        
        // Otherwise try to fetch students from API (fallback)
        console.log("Attempting to fetch students from API");
        fetch('/Dashboard/GetStudentsForEmployer')
            .then(response => {
                if (!response.ok) {
                    throw new Error('Server returned ' + response.status);
                }
                return response.json();
            })
            .then(data => {
                if (data.success && data.students && data.students.length > 0) {
                    // Clear the container first
                    containerElement.innerHTML = '';
                    
                    // Create and append student items
                    data.students.forEach(student => {
                        const studentHtml = createStudentItemHTML(student);
                        containerElement.innerHTML += studentHtml;
                    });
                    
                    // After all students are loaded, update their scores
                    setTimeout(updateStudentScoresInList, 500);
                    setTimeout(sortStudentsByScore, 1000);
                } else {
                    console.warn('No students found in API response');
                }
            })
            .catch(error => {
                console.warn('Error fetching students from API, using server-rendered list:', error);
                // If we fail to fetch, just use the existing items and update scores
                setTimeout(updateStudentScoresInList, 500);
                setTimeout(sortStudentsByScore, 1000);
            });
    }

    // Notification helper function
    function showNotification(title, message) {
        const notification = document.createElement('div');
        notification.className = 'notification ' + (title === 'Error' ? 'notification-error' : 'notification-success');
        notification.innerHTML = `
            <div class="notification-title">${title}</div>
            <div class="notification-message">${message}</div>
        `;
        document.body.appendChild(notification);
        
        // Show notification
        setTimeout(() => {
            notification.classList.add('show');
        }, 100);
        
        // Remove after 5 seconds
        setTimeout(() => {
            notification.classList.remove('show');
            setTimeout(() => {
                document.body.removeChild(notification);
            }, 300);
        }, 5000);
    }

    // Helper function to get display name for categories
    function getCategoryDisplayName(category) {
        switch(category) {
            case 'grades': return 'Top by Grades';
            case 'mastery': return 'Mastery';
            case 'webinars': return 'Webinars/Seminars';
            case 'extracurricular': return 'Extracurricular Activities';
            default: return category;
        }
    }

    // Add debugging helper
    function debugLog(message) {
        console.log(`[EmployerDashboard] ${message}`);
    }
    
    // Make sure DOM is fully loaded before attaching handlers
    document.addEventListener('DOMContentLoaded', function() {
        debugLog('DOM loaded, initializing dashboard...');
        
        // Re-attach filter handlers to ensure they work
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.addEventListener('click', function() {
                const filterType = this.getAttribute('data-filter');
                debugLog(`Filter button clicked: ${this.textContent.trim()} (${filterType})`);
                
                if (filterType === 'all') {
                    // Use the same filterByCategory function for all categories for consistency
                    filterByCategory('all');
                } else if (filterType === 'top') {
                    // Call filterByCategory for top performers
                    filterByCategory('top');
                } else if (filterType === 'grades' || filterType === 'mastery' || 
                          filterType === 'webinars' || filterType === 'extracurricular') {
                    filterByCategory(filterType);
                }
            });
        });
        
        debugLog('Filter buttons initialized');
    });

    // Function to ensure student elements have all required data attributes
    function ensureStudentDataAttributes(studentElement) {
        // Check for data-score attribute
        if (!studentElement.hasAttribute('data-score')) {
            studentElement.setAttribute('data-score', '0');
        }
        
        // Check for category-specific score attributes
        const scoreAttributes = ['data-academic-score', 'data-mastery-score', 'data-seminars-score', 'data-extracurricular-score'];
        scoreAttributes.forEach(attr => {
            if (!studentElement.hasAttribute(attr)) {
                studentElement.setAttribute(attr, '0');
            }
        });
        
        // Ensure grade level
        if (!studentElement.hasAttribute('data-grade-level')) {
            studentElement.setAttribute('data-grade-level', '1');
        }
        
        return studentElement;
    }
    
 function filterByCategory(category) {
console.log(`Filtering by category: ${category}`);

// Update active button
document.querySelectorAll('.filter-btn').forEach(btn => {
    btn.classList.remove('active');
});
document.querySelector(`.filter-btn[data-filter="${category}"]`).classList.add('active');

const studentContainer = document.getElementById('studentContainer');
if (!studentContainer) {
    console.error("Student container not found");
    return;
}

// Save all student elements to an array
const allStudents = Array.from(document.querySelectorAll('.student-item'));
console.log(`Found ${allStudents.length} total student elements`);

if (allStudents.length === 0) {
    studentContainer.innerHTML = '<p>No students found.</p>';
    return;
}

// Find senior toggle status
const seniorToggle = document.getElementById('seniorToggle');
const seniorOnly = seniorToggle && seniorToggle.checked;

// Map filter categories to the correct data attributes
const categoryToAttribute = {
    'all': 'data-score',
    'top': 'data-score',
    'grades': 'data-academic-score',  // Make sure this matches what's set in fetchStudentScore
    'mastery': 'data-mastery-score',
    'webinars': 'data-seminars-score',
    'extracurricular': 'data-extracurricular-score'
};

// Get the right attribute for filtering
const scoreAttribute = categoryToAttribute[category] || 'data-score';

// Filter students based on category and senior toggle
let visibleCount = 0;

allStudents.forEach(student => {
    // Check senior filter first
    const gradeLevel = parseInt(student.getAttribute('data-grade-level') || '1');
    const passesSeniorFilter = !seniorOnly || gradeLevel >= 4;
    
    if (!passesSeniorFilter) {
        student.style.display = 'none';
        return;
    }
    
    // Then check category filter
    let shouldShow = true;
    
    if (category === 'all') {
        shouldShow = true;
    } 
    else if (category === 'top') {
        const score = parseFloat(student.getAttribute('data-score') || '0');
        shouldShow = score >= 75; // Lower threshold to include more students
    }
    else {
        const score = parseFloat(student.getAttribute(scoreAttribute) || '0');
        // Lower threshold for categories - any score above 0 is valid
        shouldShow = score > 0;
    }
    
    student.style.display = shouldShow ? 'flex' : 'none';
    if (shouldShow) visibleCount++;
});

console.log(`Filtered by ${category}: ${visibleCount} students visible`);

// Sort visible students based on the selected category
const visibleStudents = Array.from(document.querySelectorAll('.student-item:not([style*="display: none"])'));

if (visibleStudents.length === 0) {
    // Show a more helpful message with the actual category name
    const categoryNames = {
        'all': 'All Students',
        'top': 'Top Performers',
        'grades': 'Academic Grades',
        'mastery': 'Mastery',
        'webinars': 'Webinars/Seminars',
        'extracurricular': 'Extracurricular Activities'
    };
    const displayName = categoryNames[category] || category;
    console.log(`No students match the '${displayName}' filter criteria.`);
    return;
}

// Sort students by the appropriate score
visibleStudents.sort((a, b) => {
    const scoreA = parseFloat(a.getAttribute(scoreAttribute) || '0');
    const scoreB = parseFloat(b.getAttribute(scoreAttribute) || '0');
    return scoreB - scoreA; // Higher scores first
});

// Reorder students in the DOM
visibleStudents.forEach(student => {
    studentContainer.appendChild(student);
});

// Add rank indicators to top students
visibleStudents.slice(0, 3).forEach((student, index) => {
    // Remove existing rank badges
    const existingBadge = student.querySelector('.rank-badge');
    if (existingBadge) {
        existingBadge.remove();
    }
    
    const rankBadge = document.createElement('div');
    rankBadge.className = 'rank-badge';
    rankBadge.textContent = `#${index + 1}`;
    rankBadge.style.position = 'absolute';
    rankBadge.style.top = '5px';
    rankBadge.style.right = '5px';
    rankBadge.style.background = index === 0 ? '#ffd700' : index === 1 ? '#c0c0c0' : '#cd7f32';
    rankBadge.style.color = index === 0 ? '#000' : '#fff';
    rankBadge.style.padding = '3px 6px';
    rankBadge.style.borderRadius = '3px';
    rankBadge.style.fontSize = '10px';
    rankBadge.style.fontWeight = 'bold';
    
    // Add position relative to student item if not already set
    if (student.style.position !== 'relative') {
        student.style.position = 'relative';
    }
    
    student.appendChild(rankBadge);
});
}
    // Function to diagnose and fix student data issues (can be called from console)
    function diagnoseStudentData() {
        console.log('=========== STUDENT DATA DIAGNOSIS ===========');
        
        // Check if student container exists
        const container = document.getElementById('studentContainer');
        if (!container) {
            console.error('ERROR: Student container not found!');
            return;
        }
        
        // Get all student elements
        const allStudents = document.querySelectorAll('.student-item');
        console.log(`Found ${allStudents.length} total student elements`);
        
        // Check visible vs hidden students
        const visibleStudents = document.querySelectorAll('.student-item:not([style*="display: none"])');
        console.log(`Visible students: ${visibleStudents.length}, Hidden: ${allStudents.length - visibleStudents.length}`);
        
        // Check active filter
        const activeFilter = document.querySelector('.filter-btn.active');
        const currentCategory = activeFilter ? activeFilter.getAttribute('data-filter') : 'none';
        console.log(`Current active filter: ${currentCategory || 'none'}`);
        
        // Analyze student data attributes
        const scoreAttributes = ['data-score', 'data-academic-score', 'data-mastery-score', 
                                'data-seminars-score', 'data-extracurricular-score', 'data-grade-level'];
        
        const missingAttributes = {};
        scoreAttributes.forEach(attr => {
            missingAttributes[attr] = 0;
        });
        
        // Check and fix student attributes
        console.log('Checking students for missing attributes...');
        allStudents.forEach((student, index) => {
            const id = student.getAttribute('data-student-id');
            const missing = [];
            
            scoreAttributes.forEach(attr => {
                if (!student.hasAttribute(attr)) {
                    missing.push(attr);
                    missingAttributes[attr]++;
                    
                    // Fix the issue
                    student.setAttribute(attr, '0');
                }
            });
            
            if (missing.length > 0) {
                console.warn(`Student ID ${id} is missing attributes: ${missing.join(', ')}`);
            }
            
            // Verify student scores are numbers
            scoreAttributes.forEach(attr => {
                const value = student.getAttribute(attr);
                if (isNaN(parseFloat(value))) {
                    console.warn(`Student ID ${id} has non-numeric ${attr}: "${value}"`);
                    student.setAttribute(attr, '0');
                }
            });
        });
        
        // Display missing attribute summary
        console.log('Missing attribute summary:');
        for (const attr in missingAttributes) {
            console.log(`${attr}: ${missingAttributes[attr]} students`);
        }
        
        // Force refresh of category filter
        if (activeFilter) {
            console.log('Refreshing current category filter...');
            filterByCategory(currentCategory);
        }
        
        console.log('=========== DIAGNOSIS COMPLETE ===========');
        console.log('Student data has been repaired. Refreshing category display.');
        
        return 'Diagnosis complete. All issues have been fixed.';
    }
    
    // Automatically fetch and refresh student scores when the page loads
    document.addEventListener('DOMContentLoaded', function() {
        const studentItems = document.querySelectorAll('.student-item');
        
        if (studentItems.length > 0) {
            // Process student items in batches
            const batchSize = 10;
            
            for (let i = 0; i < studentItems.length; i += batchSize) {
                const batch = Array.from(studentItems).slice(i, i + batchSize);
                
                setTimeout(() => {
                    batch.forEach(item => {
                        const studentId = item.getAttribute('data-student-id');
                        if (studentId) {
                            fetchStudentScore(studentId, item);
                        }
                    });
                }, i * 50); // Slight delay between batches to avoid overwhelming the server
            }
        }
        
        function fetchStudentScore(studentId, studentElement) {
            fetch(`/Dashboard/GetStudentScore?studentId=${studentId}`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error('Failed to fetch score');
                    }
                    return response.json();
                })
                .then(data => {
                    if (data.success) {
                        // Update score in the UI
                        const scoreElement = studentElement.querySelector(`.student-score[data-student-id="${studentId}"]`);
                        if (scoreElement) {
                            // Use overall score for display but exclude CompletedChallengesScore from calculation
                            let score = parseFloat(data.score).toFixed(2);
                            
                            // Add badge to the score display
                            const badgeColor = data.badgeColor || 'warning';
                            const badgeClass = getBadgeClass(badgeColor);
                            
                            scoreElement.innerHTML = `
                                ${score} <span class="badge ${badgeClass}">${badgeColor}</span>
                            `;
                            
                            // Store overall score
                            studentElement.setAttribute('data-score', score);
                            
                            // Set individual category scores directly from database columns
                            // These will be used for category-specific filtering and ranking
                            if (data.academicScore !== undefined) {
                                studentElement.setAttribute('data-academic-score', data.academicScore);
                                debugLog(`Student ${studentId} academic score: ${data.academicScore}`);
                            }
                            if (data.masteryScore !== undefined) {
                                studentElement.setAttribute('data-mastery-score', data.masteryScore);
                                debugLog(`Student ${studentId} mastery score: ${data.masteryScore}`);
                            }
                            if (data.seminarsScore !== undefined) {
                                studentElement.setAttribute('data-seminars-score', data.seminarsScore);
                                debugLog(`Student ${studentId} seminars score: ${data.seminarsScore}`);
                            }
                            if (data.extracurricularScore !== undefined) {
                                studentElement.setAttribute('data-extracurricular-score', data.extracurricularScore);
                                debugLog(`Student ${studentId} extracurricular score: ${data.extracurricularScore}`);
                            }
                            if (data.challengesScore !== undefined) {
                                studentElement.setAttribute('data-challenges-score', data.challengesScore);
                                debugLog(`Student ${studentId} challenges score: ${data.challengesScore}`);
                            }
                            
                            // Store grade level for filtering
                            if (data.gradeLevel !== undefined) {
                                studentElement.setAttribute('data-grade-level', data.gradeLevel);
                            }
                            
                            // Update student item background color based on new score
                            updateStudentItemStyle(studentElement, score);
                        }
                    }
                })
                .catch(error => {
                    console.error(`Error fetching score for student ${studentId}:`, error);
                    
                    // Try fallback endpoints
                    fetch(`/Score/GetStudentScoreBreakdown?studentId=${studentId}`)
                        .then(response => response.ok ? response.json() : Promise.reject('Score API failed'))
                        .then(scoreData => {
                            if (scoreData && scoreData.success) {
                                console.log(`Fetched score breakdown for ${studentId} from alternative endpoint`);
                                
                                // Get the score from any available property
                                let score = 0;
                                if (scoreData.overall && scoreData.overall.percentage !== undefined) {
                                    score = parseFloat(scoreData.overall.percentage);
                                } else if (scoreData.currentScore !== undefined) {
                                    score = parseFloat(scoreData.currentScore);
                                } else if (scoreData.totalScore !== undefined) {
                                    score = parseFloat(scoreData.totalScore);
                                }
                                
                                // Find the score element
                                const scoreElement = studentElement.querySelector(`.student-score[data-student-id="${studentId}"]`);
                                if (scoreElement) {
                                    // Update the displayed score
                                    scoreElement.innerHTML = `
                                        ${score.toFixed(2)} <span class="badge ${getBadgeClass(score)}">
                                            ${getBadgeColorText(score)}
                                        </span>
                                    `;
                                    
                                    // Set the score attribute
                                    studentElement.setAttribute('data-score', score);
                                    
                                    // Update student item style
                                    updateStudentItemStyle(studentElement, score);
                                }
                            }
                        })
                        .catch(fallbackError => {
                            console.error(`All score fetch attempts failed for student ${studentId}:`, fallbackError);
                        });
                });
        }
        
        function updateStudentItemStyle(element, score) {
            // Update the student item background color based on score
            const scoreNum = parseFloat(score);
            let containerColor;
            let pulseClass;
            
            if (scoreNum >= 95) {
                containerColor = "#b9f2ff";
                pulseClass = "student-pulse-platinum";
            } else if (scoreNum >= 85) {
                containerColor = "#ffe34f";
                pulseClass = "student-pulse-gold";
            } else if (scoreNum >= 75) {
                containerColor = "#cfcccc";
                pulseClass = "student-pulse-silver";
            } else if (scoreNum >= 65) {
                containerColor = "#f5b06c";
                pulseClass = "student-pulse-bronze";
            } else if (scoreNum >= 50) {
                containerColor = "#98fb98";
                pulseClass = "student-pulse-rising";
            } else if (scoreNum >= 1) {
                containerColor = "#ffcccb";
                pulseClass = "student-pulse-needs";
            } else {
                containerColor = "#ffffff";
                pulseClass = "student-pulse-none";
            }
            
            // Remove all pulse classes
            element.classList.remove(
                "student-pulse-platinum", 
                "student-pulse-gold", 
                "student-pulse-silver", 
                "student-pulse-bronze", 
                "student-pulse-rising", 
                "student-pulse-needs", 
                "student-pulse-none"
            );
            
            // Add the appropriate pulse class
            element.classList.add(pulseClass);
            
            // Set the background color
            element.style.backgroundColor = containerColor;
        }
    });

    // Utility function to validate category scoring (can be called from browser console)
    function validateCategoryScoring(category) {
        console.log(`=== VALIDATING ${category.toUpperCase()} CATEGORY SCORING ===`);
        
        // Find the students currently visible
        const visibleStudents = document.querySelectorAll('.student-item:not([style*="display: none"])');
        console.log(`Found ${visibleStudents.length} visible students`);
        
        if (visibleStudents.length === 0) {
            console.log('No visible students to validate');
            return;
        }
        
        // Get the score attribute for this category
        let scoreAttribute = 'data-score';
        switch(category) {
            case 'grades': scoreAttribute = 'data-academic-score'; break;
            case 'mastery': scoreAttribute = 'data-mastery-score'; break;
            case 'webinars': scoreAttribute = 'data-seminars-score'; break;
            case 'extracurricular': scoreAttribute = 'data-extracurricular-score'; break;
        }
        
        // Check if students are properly sorted by the category score
        const students = Array.from(visibleStudents);
        let correctlySorted = true;
        
        for (let i = 0; i < students.length - 1; i++) {
            const currentScore = parseFloat(students[i].getAttribute(scoreAttribute) || '0');
            const nextScore = parseFloat(students[i+1].getAttribute(scoreAttribute) || '0');
            
            if (currentScore < nextScore) {
                console.error(`SORTING ERROR: Student at position ${i} has lower ${category} score (${currentScore}) than student at position ${i+1} (${nextScore})`);
                correctlySorted = false;
            }
        }
        
        if (correctlySorted) {
            console.log(`✅ CORRECT: Students are properly sorted by ${category} score`);
        } else {
            console.error(`❌ ERROR: Students are NOT properly sorted by ${category} score`);
        }
        
        // Display the top 5 students and their scores
        console.log('Top 5 students by category score:');
        for (let i = 0; i < Math.min(5, students.length); i++) {
            const student = students[i];
            const name = student.querySelector('h3')?.textContent || 'Unknown';
            const id = student.getAttribute('data-student-id');
            const score = student.getAttribute(scoreAttribute);
            
            // Also get other scores for comparison
            const overallScore = student.getAttribute('data-score');
            const academicScore = student.getAttribute('data-academic-score');
            const masteryScore = student.getAttribute('data-mastery-score');
            const webinarScore = student.getAttribute('data-seminars-score');
            const extracurricularScore = student.getAttribute('data-extracurricular-score');
            
            console.log(`${i+1}. ${name} (ID: ${id})`);
            console.log(`   - Category score (${category}): ${score}`);
            console.log(`   - Other scores: Overall=${overallScore}, Academic=${academicScore}, Mastery=${masteryScore}, Webinars=${webinarScore}, Extracurricular=${extracurricularScore}`);
        }
        
        console.log('=== VALIDATION COMPLETE ===');
        return correctlySorted;
    }

// Add these functions to your JavaScript
function viewResumeInModal(studentId) {
    // Set the iframe source to the resume URL
    const resumeFrame = document.getElementById('resumeViewFrame');
    resumeFrame.src = `/FileHandler/GetResume?studentId=${studentId}`;
    
    // Show the modal
    document.getElementById('resumeViewModal').style.display = 'flex';
    document.body.classList.add('modal-open');
}

function closeResumeView() {
    document.getElementById('resumeViewModal').style.display = 'none';
    document.body.classList.remove('modal-open');
    // Clear the iframe source to stop any audio or video that might be playing
    document.getElementById('resumeViewFrame').src = '';
}

// Close the resume view modal when clicking outside
window.onclick = function(event) {
    const resumeModal = document.getElementById('resumeViewModal');
    if (event.target == resumeModal) {
        closeResumeView();
    }
}

// Mark/unmark student - simplified version without modal
function markStudent(studentId, studentName) {
// Check if student is already marked
$.ajax({
    url: '/Dashboard/GetMarkedStudents',
    type: 'GET',
    success: function(response) {
        if (response.success) {
            const markedStudents = response.markedStudents || [];
            const isMarked = markedStudents.some(s => s.studentId === studentId);
            
            if (isMarked) {
                if (confirm(`Unmark ${studentName}?`)) {
                    // Unmark the student
                    $.ajax({
                        url: '/Dashboard/UnmarkStudent',
                        type: 'POST',
                        data: { studentId: studentId },
                        success: function(response) {
                            if (response.success) {
                                alert('Student unmarked');
                                // Update bookmark icon
                                updateBookmarkIcon(studentId, false);
                            } else {
                                alert(response.message || 'Error unmarking student');
                            }
                        },
                        error: function() {
                            alert('Error unmarking student');
                        }
                    });
                }
            } else {
                // Directly mark the student without notes
                $.ajax({
                    url: '/Dashboard/MarkStudent',
                    type: 'POST',
                    data: { 
                        studentId: studentId,
                        notes: "" // Empty notes
                    },
                    success: function(response) {
                        if (response.success) {
                            alert('Student marked successfully');
                            // Update bookmark icon
                            updateBookmarkIcon(studentId, true);
                        } else {
                            alert(response.message || 'Error marking student');
                        }
                    },
                    error: function() {
                        alert('Error marking student');
                    }
                });
            }
        } else {
            alert(response.message || 'Error checking marked status');
        }
    },
    error: function() {
        alert('Error checking marked status');
    }
});
}

function updateBookmarkIcon(studentId, isMarked) {
const button = $(`.mark-student-btn[onclick*="${studentId}"]`);
if (button.length) {
    if (isMarked) {
        button.addClass('marked');
        button.find('i').removeClass('far').addClass('fas');
    } else {
        button.removeClass('marked');
        button.find('i').removeClass('fas').addClass('far');
    }
}
}

// Check which students are already marked when page loads
$(document).ready(function() {
$.ajax({
    url: '/Dashboard/GetMarkedStudents',
    type: 'GET',
    success: function(response) {
        if (response.success && response.markedStudents) {
            // Update bookmark icons for marked students
            response.markedStudents.forEach(student => {
                updateBookmarkIcon(student.studentId, true);
            });
        }
    }
});
});
function promptForNotes(studentId, studentName) {
// Create notes prompt modal if it doesn't exist
if (!$('#markStudentModal').length) {
    $('body').append(`
        <div class="modal fade" id="markStudentModal" tabindex="-1" role="dialog">
            <div class="modal-dialog" role="document">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Mark Student</h5>
                        <button type="button" class="close" data-dismiss="modal">
                            <span>&times;</span>
                        </button>
                    </div>
                    <div class="modal-body">
                        <p>Add notes about <span id="studentNamePlaceholder"></span>:</p>
                        <textarea id="studentNotes" class="form-control" rows="4" placeholder="Optional notes about this student"></textarea>
                        <input type="hidden" id="markStudentId">
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" data-dismiss="modal">Cancel</button>
                        <button type="button" class="btn btn-primary" id="saveMarkBtn">Mark Student</button>
                    </div>
                </div>
            </div>
        </div>
    `);
    
    // Add event listener for the save button
    $('#saveMarkBtn').click(function() {
        const id = $('#markStudentId').val();
        const notes = $('#studentNotes').val();
        
        // Mark the student
        $.ajax({
            url: '/Dashboard/MarkStudent',
            type: 'POST',
            data: { 
                studentId: id,
                notes: notes
            },
            success: function(response) {
                if (response.success) {
                    alert('Student marked successfully');
                    $('#markStudentModal').modal('hide');
                    // Update bookmark icon
                    updateBookmarkIcon(id, true);
                } else {
                    alert(response.message);
                }
            },
            error: function() {
                alert('Error marking student');
            }
        });
    });
}

// Set values in the modal
$('#studentNamePlaceholder').text(studentName);
$('#markStudentId').val(studentId);
$('#studentNotes').val('');

// Show the modal
$('#markStudentModal').modal('show');
}

function updateBookmarkIcon(studentId, isMarked) {
const button = $(`.mark-student-btn[onclick*="${studentId}"]`);
if (isMarked) {
    button.addClass('marked');
    button.find('i').removeClass('far').addClass('fas');
} else {
    button.removeClass('marked');
    button.find('i').removeClass('fas').addClass('far');
}
}

// Check which students are already marked when page loads
$(document).ready(function() {
$.ajax({
    url: '/Dashboard/GetMarkedStudents',
    type: 'GET',
    success: function(response) {
        if (response.success && response.markedStudents) {
            // Update bookmark icons for marked students
            response.markedStudents.forEach(student => {
                updateBookmarkIcon(student.studentId, true);
            });
        }
    }
});
});

    // Handle call response from student
    function handleCallResponse(callId, status) {
        if (callId != activeCallId) return;
        
        if (status === 'accepted') {
            // Redirect to video call page
            window.location.href = `/VideoCall/EmployerVideoCall?callId=${callId}`;
        } else if (status === 'declined') {
            document.getElementById('videoCallStatus').textContent = 'Call declined by student.';
            document.getElementById('cancelCallBtn').textContent = 'Close';
            currentVideoCallStatus = 'declined';
        }
    }

    // Initiate video call with a student
    async function initiateVideoCall(studentId, studentName) {
        currentVideoCallStudentId = studentId;
        currentVideoCallStatus = 'initiating';
        
        try {
            // Fetch student details first
            const response = await fetch(`/Dashboard/GetStudentBasicInfo?studentId=${studentId}`);
            const data = await response.json();
            
            console.log("Video call student data:", data);
            
            // Update UI with fallbacks for missing data
            document.getElementById('videoCallStudentName').textContent = studentName || "Student";
            
            let profilePic = '/images/blank.jpg';
            if (data.success && data.student && data.student.profilePicturePath) {
                profilePic = data.student.profilePicturePath;
            }
            
            document.getElementById('videoCallStudentImg').src = profilePic;
            document.getElementById('videoCallStatus').textContent = 'Ready to call student';
            document.getElementById('startCallBtn').style.display = 'inline-block';
            document.getElementById('cancelCallBtn').textContent = 'Cancel';
            
            // Show modal
            document.getElementById('videoCallModal').style.display = 'block';
        } catch (error) {
            console.error('Error fetching student details:', error);
            // Still show the modal with default image if fetch fails
            document.getElementById('videoCallStudentName').textContent = studentName || "Student";
            document.getElementById('videoCallStudentImg').src = '/images/blank.jpg';
            document.getElementById('videoCallStatus').textContent = 'Ready to call student';
            document.getElementById('startCallBtn').style.display = 'inline-block';
            document.getElementById('cancelCallBtn').textContent = 'Cancel';
            document.getElementById('videoCallModal').style.display = 'block';
        }
    }

    // Start the video call (send request to student)
    async function startVideoCall() {
        if (currentVideoCallStatus !== 'initiating') return;
        
        try {
            document.getElementById('videoCallStatus').textContent = 'Calling student...';
            document.getElementById('startCallBtn').style.display = 'none';
            
            // Check if connection is established
            if (!connection || connection.state !== "Connected") {
                document.getElementById('videoCallStatus').textContent = 'Connection not established. Please refresh the page and try again.';
                document.getElementById('cancelCallBtn').textContent = 'Close';
                return;
            }
            
            // Get the employer ID from the hidden field or data attribute in the page
            // This replaces the Razor syntax @jsEmployerId which doesn't work in separate JS files
            let employerId;
            
            // Try multiple options to get the employer ID with better validation
            if (document.getElementById('employerId') && document.getElementById('employerId').value) {
                employerId = document.getElementById('employerId').value;
                console.log("Found employer ID from hidden input:", employerId);
            } else if (document.body.getAttribute('data-employer-id')) {
                employerId = document.body.getAttribute('data-employer-id');
                console.log("Found employer ID from body attribute:", employerId);
            } else if (window.employerId) {
                employerId = window.employerId;
                console.log("Found employer ID from global variable:", employerId);
            } else {
                document.getElementById('videoCallStatus').textContent = 'Cannot identify your account. Please refresh the page.';
                document.getElementById('cancelCallBtn').textContent = 'Close';
                return;
            }
            
            // Reject calls with invalid employer ID
            if (!employerId || employerId === "@jsEmployerId") {
                document.getElementById('videoCallStatus').textContent = 'Invalid employer ID. Please refresh the page.';
                document.getElementById('cancelCallBtn').textContent = 'Close';
                return;
            }
            
            // Request a call with the student with a timeout
            const callTimeout = setTimeout(() => {
                document.getElementById('videoCallStatus').textContent = 'Call request timed out. Please try again.';
                document.getElementById('cancelCallBtn').textContent = 'Close';
                currentVideoCallStatus = 'error';
            }, 15000); // 15 second timeout
            
            await connection.invoke("RequestCall", employerId, currentVideoCallStudentId);
            clearTimeout(callTimeout);
            
            currentVideoCallStatus = 'calling';
        } catch (error) {
            console.error("Error starting video call:", error);
            document.getElementById('videoCallStatus').textContent = `Error: ${error.message}`;
            document.getElementById('startCallBtn').style.display = 'inline-block';
        }
    }

    // Function to open edubadge certificate for students
    function openEdubadgeCertificate(studentId, studentName) {
        // Open the certificate page in a new tab
        window.open(`/ProgrammingTest/ViewEduBadgeCertificate?studentId=${studentId}`, '_blank');
    }

    // Helper function to get badge color text based on score
    function getBadgeColorText(score) {
        score = parseFloat(score);
        if (isNaN(score)) return 'warning';
        
        if (score >= 95) return 'platinum';
        else if (score >= 85) return 'gold';
        else if (score >= 75) return 'silver';
        else if (score >= 65) return 'bronze';
        else if (score >= 50) return 'rising-star';
        else return 'warning';
    }

    // Function to ensure pulse animations are applied
    function ensurePulseAnimations() {
        console.log("Ensuring pulse animations are applied to all students");
        const studentItems = document.querySelectorAll('.student-item');
        
        studentItems.forEach(item => {
            // Get the score for this student
            const score = parseFloat(item.getAttribute('data-score') || '0');
            
            // Remove any existing pulse classes first
            item.classList.remove(
                'student-pulse-platinum', 
                'student-pulse-gold', 
                'student-pulse-silver', 
                'student-pulse-bronze', 
                'student-pulse-rising', 
                'student-pulse-needs'
            );
            
            // Assign the appropriate pulse class based on score
            let pulseClass = "";
            if (score >= 95) pulseClass = "student-pulse-platinum";
            else if (score >= 85) pulseClass = "student-pulse-gold";
            else if (score >= 75) pulseClass = "student-pulse-silver";
            else if (score >= 65) pulseClass = "student-pulse-bronze";
            else if (score >= 50) pulseClass = "student-pulse-rising";
            else pulseClass = "student-pulse-needs";
            
            // Add the pulse class
            item.classList.add(pulseClass);
            
            // Also add a specific style to ensure the animation works
            item.style.position = 'relative';
            item.style.zIndex = '1';
            
            // Force browser to recalculate animations
            // This trick helps to restart animations that might have stalled
            item.style.animation = 'none';
            setTimeout(() => {
                item.style.animation = '';
            }, 10);
            
            console.log(`Applied pulse class ${pulseClass} to student with score ${score}`);
        });
    }
    
    // When document is ready, set up event handlers and load initial data
    $(document).ready(function() {
        // Initialize filter buttons - REMOVED DUPLICATE HANDLER
        
        // Ensure all profile pictures have error handling
        ensureProfileImageErrorHandling();
        
        // Load initial data and update scores
        loadInitialData();
        
        // Ensure pulse animations are applied
        setTimeout(ensurePulseAnimations, 1000);
    });
