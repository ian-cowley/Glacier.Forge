document.addEventListener('DOMContentLoaded', () => {
    // Smooth scrolling for navigation links
    const navLinks = document.querySelectorAll('a[href^="#"]');
    navLinks.forEach(link => {
        link.addEventListener('click', function(e) {
            e.preventDefault();
            const targetId = this.getAttribute('href');
            if (targetId === '#') return;

            const targetElement = document.querySelector(targetId);
            if (targetElement) {
                const navbarHeight = document.querySelector('.navbar').offsetHeight;
                const targetPosition = targetElement.getBoundingClientRect().top + window.pageYOffset - navbarHeight;

                window.scrollTo({
                    top: targetPosition,
                    behavior: 'smooth'
                });
            }
        });
    });

    // Console message to showcase Glacier software performance values
    console.log(
        '%c GLACIER SOFTWARE %c Active v1.0 Ingestion Pipeline initialized %c',
        'background: #0b0c10; color: #66fcf1; padding: 4px 6px; font-weight: bold; border-radius: 4px 0 0 4px;',
        'background: #1f2833; color: #f8f9fa; padding: 4px 6px; border-radius: 0 4px 4px 0;',
        'background: transparent;'
    );
});
