import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { getProduct, getAlsoPurchased, addToCart, getReviews, createReview } from '../api/client';
import type { Product, Review } from '../api/client';
import RecommendationStrip from '../components/RecommendationStrip';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function ProductPage() {
  const { id } = useParams<{ id: string }>();
  const { currentUser } = usePersona();
  const { t, formatCurrency } = useLocale();
  const [product, setProduct] = useState<Product | null>(null);
  const [recs, setRecs] = useState<Product[]>([]);
  const [toast, setToast] = useState('');
  const [reviews, setReviews] = useState<Review[]>([]);
  const [rating, setRating] = useState(5);
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const reviewRequestId = useRef(0);

  const loadReviews = useCallback((productId: string) => {
    const requestId = ++reviewRequestId.current;
    getReviews(productId)
      .then(nextReviews => {
        if (reviewRequestId.current === requestId) setReviews(nextReviews);
      })
      .catch(() => {
        if (reviewRequestId.current === requestId) setReviews([]);
      });
  }, []);

  useEffect(() => {
    if (!id) return;
    getProduct(id).then(setProduct).catch(() => {});
    getAlsoPurchased(id).then(setRecs).catch(() => {});
    loadReviews(id);
  }, [id, loadReviews]);

  async function handleAddToCart() {
    if (!product || !currentUser) return;
    try {
      await addToCart(currentUser.id, product.id, 1);
      setToast(t('product.addedToCart'));
      setTimeout(() => setToast(''), 3000);
    } catch { setToast(t('product.addFailed')); }
  }

  async function handleSubmitReview(e: React.FormEvent) {
    e.preventDefault();
    if (!product || !currentUser || !title.trim()) return;
    setSubmitting(true);
    try {
      await createReview(product.id, { userId: currentUser.id, title: title.trim(), body: body.trim(), rating });
      setTitle('');
      setBody('');
      setRating(5);
      setToast(t('reviews.submitted'));
      setTimeout(() => setToast(''), 3000);
      loadReviews(product.id);
    } catch {
      setToast(t('reviews.submitFailed'));
      setTimeout(() => setToast(''), 3000);
    } finally {
      setSubmitting(false);
    }
  }

  if (!product) return <div data-testid="product-loading">{t('product.loading')}</div>;

  const average = reviews.length
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : 0;

  return (
    <div data-testid="product-page">
      <img src={product.imageUrl || 'https://placehold.co/400x300'} alt={product.name} className="product-detail-img" />
      <h1 data-testid="product-name">{product.name}</h1>
      <div data-testid="product-price">{formatCurrency(product.price, product.currency)}</div>
      <p data-testid="product-desc">{product.description}</p>
      <div>{t('product.category')}: {product.category}</div>
      <div>{t('product.stock')}: {product.stock}</div>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="product-toast">{toast}</div>}
      </div>
      <button data-testid="product-add-cart" onClick={handleAddToCart} disabled={!currentUser}>{t('product.addToCart')}</button>
      <RecommendationStrip products={recs} />

      <section data-testid="reviews-section">
        <h2>{t('reviews.title')}</h2>
        {reviews.length === 0 ? (
          <div data-testid="reviews-empty">{t('reviews.empty')}</div>
        ) : (
          <>
            <div data-testid="reviews-average">
              {t('reviews.average')}: {average.toFixed(1)} / 5 ({t('reviews.count', { count: String(reviews.length) })})
            </div>
            <ul data-testid="reviews-list">
              {reviews.map(review => (
                <li key={review.id} data-testid={`review-${review.id}`}>
                  <strong>{review.title}</strong>
                  <span data-testid={`review-rating-${review.id}`}> — {review.rating} / 5</span>
                  <p>{review.body}</p>
                </li>
              ))}
            </ul>
          </>
        )}

        {currentUser ? (
          <form data-testid="review-form" onSubmit={handleSubmitReview}>
            <h3>{t('reviews.formTitle')}</h3>
            <label>
              {t('reviews.rating')}
              <select data-testid="review-rating" value={rating} onChange={e => setRating(Number(e.target.value))}>
                {[5, 4, 3, 2, 1].map(value => <option key={value} value={value}>{value}</option>)}
              </select>
            </label>
            <label htmlFor="review-title">{t('reviews.titlePlaceholder')}</label>
            <input
              id="review-title"
              data-testid="review-title"
              placeholder={t('reviews.titlePlaceholder')}
              value={title}
              onChange={e => setTitle(e.target.value)}
              required
            />
            <textarea
              data-testid="review-body"
              placeholder={t('reviews.bodyPlaceholder')}
              value={body}
              onChange={e => setBody(e.target.value)}
            />
            <button data-testid="review-submit" type="submit" disabled={submitting || !title.trim()}>
              {submitting ? t('reviews.submitting') : t('reviews.submit')}
            </button>
          </form>
        ) : (
          <div data-testid="review-no-user">{t('reviews.noUser')}</div>
        )}
      </section>
    </div>
  );
}
