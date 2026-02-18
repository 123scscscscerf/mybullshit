<?php

declare(strict_types=1);

require_once __DIR__ . '/db.php';

function startSecureSession(): void
{
    if (session_status() === PHP_SESSION_NONE) {
        session_set_cookie_params([
            'httponly' => true,
            'secure' => isset($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off',
            'samesite' => 'Lax',
        ]);
        session_start();
    }
}

function isAdminLoggedIn(): bool
{
    startSecureSession();

    return !empty($_SESSION['admin_logged_in']);
}

function attemptLogin(string $username, string $password): bool
{
    $config = appConfig();
    startSecureSession();

    if (
        hash_equals($config['admin']['username'], $username)
        && password_verify($password, $config['admin']['password_hash'])
    ) {
        session_regenerate_id(true);
        $_SESSION['admin_logged_in'] = true;

        return true;
    }

    return false;
}

function requireAdmin(): void
{
    if (!isAdminLoggedIn()) {
        header('Location: /admin/index.php');
        exit;
    }
}

function logoutAdmin(): void
{
    startSecureSession();
    $_SESSION = [];

    if (ini_get('session.use_cookies')) {
        $params = session_get_cookie_params();
        setcookie(session_name(), '', time() - 42000, $params['path'], $params['domain'], $params['secure'], $params['httponly']);
    }

    session_destroy();
}
