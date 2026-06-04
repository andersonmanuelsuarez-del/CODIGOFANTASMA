document.addEventListener('DOMContentLoaded', () => {
    // Create lightbox HTML
    const lightboxHTML = `
        <div class="lightbox-overlay" id="lightboxOverlay">
            <span class="lightbox-close" id="lightboxClose">&times;</span>
            <img class="lightbox-img" id="lightboxImg" src="" alt="Zoomed image">
        </div>
    `;
    document.body.insertAdjacentHTML('beforeend', lightboxHTML);

    const overlay = document.getElementById('lightboxOverlay');
    const lightboxImg = document.getElementById('lightboxImg');
    const closeBtn = document.getElementById('lightboxClose');

    // Add click event to all zoomable images
    document.querySelectorAll('.zoomable-image').forEach(img => {
        img.addEventListener('click', (e) => {
            lightboxImg.src = e.target.src;
            overlay.classList.add('active');
        });
    });

    // Close logic
    const closeLightbox = () => overlay.classList.remove('active');
    closeBtn.addEventListener('click', closeLightbox);
    overlay.addEventListener('click', (e) => {
        if (e.target === overlay) closeLightbox();
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') closeLightbox();
    });
});
