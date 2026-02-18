# the.sytes.net (v2 – file server edition)

Simple dark-themed file server using PHP 8+, SQLite, and Apache2 with `mod_php`.

## Project structure

```text
.
├── .htaccess
├── admin/
│   └── index.php
├── files/
│   └── .htaccess
├── includes/
│   ├── auth.php
│   ├── config.php
│   └── db.php
├── public/
│   ├── about.php
│   ├── download.php
│   ├── index.php
│   └── style.css
├── data/
│   └── database.sqlite (auto-created)
└── schema.sql
```

## Installation steps

1. Install Apache2 + PHP 8+ modules:
   ```bash
   sudo apt update
   sudo apt install apache2 php php-sqlite3 libapache2-mod-php
   ```
2. Copy this project to your server, e.g. `/var/www/the.sytes.net`.
3. Enable rewrite module:
   ```bash
   sudo a2enmod rewrite
   sudo systemctl restart apache2
   ```
4. Ensure Apache virtual host allows `.htaccess` (`AllowOverride All`).
5. Set proper permissions:
   ```bash
   sudo chown -R www-data:www-data /var/www/the.sytes.net
   sudo chmod -R 755 /var/www/the.sytes.net
   sudo chmod -R 775 /var/www/the.sytes.net/files /var/www/the.sytes.net/data
   ```
6. Open `http://your-host/` and login at `/admin/index.php`.

## Apache config example

```apache
<VirtualHost *:80>
    ServerName the.sytes.net
    DocumentRoot /var/www/the.sytes.net

    <Directory /var/www/the.sytes.net>
        AllowOverride All
        Require all granted
    </Directory>

    ErrorLog ${APACHE_LOG_DIR}/the.sytes.net-error.log
    CustomLog ${APACHE_LOG_DIR}/the.sytes.net-access.log combined
</VirtualHost>
```

## Required permissions

- Web server user (`www-data`) needs write permission to:
  - `files/` (uploaded file storage)
  - `data/` (SQLite DB file)
- `files/.htaccess` must remain in place to block direct file access.

## Change admin credentials

1. Edit `includes/config.php`.
2. Update `'username'`.
3. Replace `'password_hash'` using:
   ```bash
   php -r "echo password_hash('YourNewPassword!', PASSWORD_DEFAULT), PHP_EOL;"
   ```

## Deploy on the.sytes.net

1. Point DNS `A`/`AAAA` for `the.sytes.net` to your server IP.
2. Enable virtual host:
   ```bash
   sudo a2ensite the.sytes.net.conf
   sudo systemctl reload apache2
   ```
3. (Recommended) Enable HTTPS with Let's Encrypt:
   ```bash
   sudo apt install certbot python3-certbot-apache
   sudo certbot --apache -d the.sytes.net
   ```

## Notes

- File downloads are routed through `public/download.php` only.
- File IDs are sanitized and can be auto-generated (`id#########`).
- SQL schema is in `schema.sql`; SQLite auto-initialization also runs in `includes/db.php`.
