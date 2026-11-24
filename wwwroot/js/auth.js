// Shared by the Login and Create Account pages.

// Show / hide password.
// Login calls togglePass() with no arguments (single password field).
// Create Account calls togglePass('Input_Password', this).
function togglePass(inputId, btn) {
    const passInput = document.getElementById(inputId || 'password');
    const button = btn || document.querySelector('.toggle-password');
    const eyeIcon = button.querySelector('svg');

    if (passInput.type === 'password') {
        passInput.type = 'text';
        eyeIcon.innerHTML = '<path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"></path><line x1="1" y1="1" x2="23" y2="23"></line>';
    } else {
        passInput.type = 'password';
        eyeIcon.innerHTML = '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle>';
    }
}

// Custom role dropdown
function toggleDropdown() {
    document.getElementById('role-dropdown').classList.toggle('open');
}

function selectRole(roleValue, element) {
    document.getElementById('role-input').value = roleValue;

    const textSpan = document.getElementById('role-selected-text');
    textSpan.textContent = roleValue;

    const trigger = document.querySelector('.custom-select-trigger');
    trigger.classList.add('has-value');

    document.querySelectorAll('.custom-option').forEach(opt =>
        opt.classList.remove('selected'));
    element.classList.add('selected');

    document.getElementById('role-dropdown').classList.remove('open');
}

document.addEventListener('click', function (e) {
    const dropdown = document.getElementById('role-dropdown');
    if (dropdown && !dropdown.contains(e.target)) {
        dropdown.classList.remove('open');
    }
});