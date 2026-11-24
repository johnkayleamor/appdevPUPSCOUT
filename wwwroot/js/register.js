// Client-side validation for the Create Account page.
// The same rules are enforced on the server in Register.cshtml.cs.
(function () {
    "use strict";

    var form = document.querySelector("form");
    if (!form) return;

    function field(name) {
        return form.elements["Input." + name] || null;
    }

    var rules = {
        FullName: function (v) {
            return v.trim() ? "" : "Enter your full name.";
        },
        Email: function (v) {
            return /^[^\s@]+@(iskolarngbayan\.)?pup\.edu\.ph$/i.test(v.trim())
                ? ""
                : "Use your @pup.edu.ph or @iskolarngbayan.pup.edu.ph email.";
        },
        IdNumber: function (v) {
            return /^\d{4}-\d{5}-[A-Za-z]{2}-\d$/.test(v.trim())
                ? ""
                : "Use the format 2026-00000-MN-0.";
        },
        Role: function (v) {
            return v ? "" : "Select your role.";
        },
        College: function (v) {
            return v.trim() ? "" : "Enter your college or department.";
        },
        Password: function (v) {
            return v.length >= 8 ? "" : "Password must be at least 8 characters.";
        },
        ConfirmPassword: function (v) {
            var pw = field("Password");
            return pw && v === pw.value ? "" : "Passwords do not match.";
        }
    };

    function check(name) {
        var input = field(name);
        if (!input) return true;

        var message = rules[name](input.value);
        var group = input.closest(".form-group");
        var error = group.querySelector(".field-error");
        if (error) error.textContent = message;

        // The role is a hidden input, so the visible box gets the red border.
        var box = name === "Role"
            ? group.querySelector(".custom-select-trigger")
            : input;
        if (box) box.classList.toggle("is-invalid", !!message);

        return !message;
    }

    Object.keys(rules).forEach(function (name) {
        var input = field(name);
        if (!input || name === "Role") return;
        input.addEventListener("blur", function () { check(name); });
        input.addEventListener("input", function () {
            if (input.classList.contains("is-invalid")) check(name);
        });
    });

    // Re-check the role after a choice is made in the dropdown.
    document.querySelectorAll(".custom-option").forEach(function (opt) {
        opt.addEventListener("click", function () { check("Role"); });
    });

    form.addEventListener("submit", function (e) {
        var valid = true;
        Object.keys(rules).forEach(function (name) {
            if (!check(name)) valid = false;
        });
        if (!valid) {
            e.preventDefault();
            var firstInvalid = form.querySelector(".is-invalid");
            if (firstInvalid && firstInvalid.focus) firstInvalid.focus();
        }
    });
})();