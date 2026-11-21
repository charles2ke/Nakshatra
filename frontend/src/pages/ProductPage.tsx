import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  getProduct, getAlsoPurchased, addToCart, getReviews, createReview, voteReviewHelpful,
  getQuestions, askQuestion, answerQuestion, getStockAlert, subscribeStockAlert, unsubscribeStockAlert,
  addToWishList, errorMessage,
} from '../api/client';
import type { Product, ProductQuestion, Review } from '../api/client';
import RecommendationStrip from '../components/RecommendationStrip';
import PriceTag from '../components/PriceTag';
import StarRating from '../components/StarRating';
import StockNotice from '../components/StockNotice';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';
import { recordRecentlyViewed } from '../hooks/useRecentlyViewed';

const MAX_QUANTITY = 10;
const STAR_LEVELS = [5, 4, 3, 2, 1];
type ReviewSort = 'recent' | 'helpful' | 'highest' | 'lowest';

export default function ProductPage() {
  const { id } = useParams<{ id: string }>();
  // Remount for each product so per-product state (quantity, review filters, drafts) starts afresh.
  return id ? <ProductDetail key={id} productId={id} /> : null;
}

function ProductDetail({ productId }: { productId: string }) {
  const { currentUser } = usePersona();
  const { t, formatDate, formatNumber } = useLocale();
  const { syncCart } = useBadges();
  const navigate = useNavigate();
  const { toast, showToast } = useToast();
  const [product, setProduct] = useState<Product | null>(null);
  const [recs, setRecs] = useState<Product[]>([]);
  const [quantity, setQuantity] = useState(1);
  const [wishListState, setWishListState] = useState<'idle' | 'saving' | 'added'>('idle');
  // Remembers whose alert it is, so switching persona never shows someone else's subscription.
  const [stockAlert, setStockAlert] = useState<{ userId: string; subscribed: boolean } | null>(null);
  const [alertBusy, setAlertBusy] = useState(false);

  const [reviews, setReviews] = useState<Review[]>([]);
  const [rating, setRating] = useState(5);
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [reviewSort, setReviewSort] = useState<ReviewSort>('recent');
  const [starFilter, setStarFilter] = useState<number | null>(null);
  const [verifiedOnly, setVerifiedOnly] = useState(false);
  // The service never reveals who voted, so remember this browser's votes as "userId:reviewId".
  const [helpfulVotes, setHelpfulVotes] = useState<Set<string>>(() => new Set());
  const reviewRequestId = useRef(0);

  const [questions, setQuestions] = useState<ProductQuestion[]>([]);
  const [questionText, setQuestionText] = useState('');
  const [answerDrafts, setAnswerDrafts] = useState<Record<string, string>>({});
  const [qaBusy, setQaBusy] = useState(false);

  const loadReviews = useCallback(() => {
    const requestId = ++reviewRequestId.current;
    getReviews(productId)
      .then(nextReviews => {
        if (reviewRequestId.current === requestId) setReviews(nextReviews);
      })
      .catch(() => {
        if (reviewRequestId.current === requestId) setReviews([]);
      });
  }, [productId]);

  useEffect(() => {
    getProduct(productId).then(setProduct).catch(() => {});
    getAlsoPurchased(productId).then(setRecs).catch(() => {});
    getQuestions(productId).then(setQuestions).catch(() => {});
    loadReviews();
  }, [productId, loadReviews]);

  const userId = currentUser?.id;
  useEffect(() => {
    if (product) recordRecentlyViewed(userId, product.id);
  }, [product, userId]);

  const outOfStock = !!product && product.stock <= 0;
  useEffect(() => {
    if (!outOfStock || !userId) return;
    let current = true;
    getStockAlert(productId, userId)
      .then(r => { if (current) setStockAlert({ userId, subscribed: r.subscribed }); })
      .catch(() => {});
    return () => { current = false; };
  }, [outOfStock, productId, userId]);

  const visibleReviews = useMemo(() => {
    const shown = reviews.filter(r => (starFilter === null || r.rating === starFilter) && (!verifiedOnly || r.verifiedPurchase));
    // The service returns the newest first; sorting is stable, so ties stay newest first.
    if (reviewSort === 'helpful') shown.sort((a, b) => (b.helpfulCount ?? 0) - (a.helpfulCount ?? 0));
    if (reviewSort === 'highest') shown.sort((a, b) => b.rating - a.rating);
    if (reviewSort === 'lowest') shown.sort((a, b) => a.rating - b.rating);
    return shown;
  }, [reviews, starFilter, verifiedOnly, reviewSort]);

  const canShop = !!currentUser && (currentUser.persona === 'Customer' || currentUser.persona === 'Admin');

  async function handleAddToCart() {
    if (!product || !currentUser) return;
    try {
      const res = await addToCart(currentUser.id, product.id, quantity);
      syncCart(res.cart);
      showToast(t('product.addedToCart'));
    } catch { showToast(t('product.addFailed')); }
  }

  function handleBuyNow() {
    if (!product) return;
    navigate(`/checkout?buyNow=${encodeURIComponent(product.id)}&qty=${quantity}`);
  }

  async function handleAddToWishList() {
    if (!product || !currentUser) return;
    setWishListState('saving');
    try {
      await addToWishList(currentUser.id, product.id);
      setWishListState('added');
      showToast(t('product.addedToWishList'));
    } catch (e) {
      setWishListState('idle');
      showToast(errorMessage(e));
    }
  }

  async function handleStockAlert(subscribe: boolean) {
    if (!currentUser) return;
    setAlertBusy(true);
    try {
      if (subscribe) await subscribeStockAlert(productId, currentUser.id);
      else await unsubscribeStockAlert(productId, currentUser.id);
      setStockAlert({ userId: currentUser.id, subscribed: subscribe });
      showToast(subscribe ? t('stockAlert.subscribedToast') : t('stockAlert.cancelledToast'));
    } catch (e) { showToast(errorMessage(e)); }
    finally { setAlertBusy(false); }
  }

  async function handleSubmitReview(e: FormEvent) {
    e.preventDefault();
    if (!product || !currentUser || !title.trim()) return;
    setSubmitting(true);
    try {
      await createReview(product.id, { userId: currentUser.id, title: title.trim(), body: body.trim(), rating });
      setTitle('');
      setBody('');
      setRating(5);
      showToast(t('reviews.submitted'));
      loadReviews();
    } catch {
      showToast(t('reviews.submitFailed'));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleHelpful(review: Review) {
    if (!currentUser) return;
    try {
      const updated = await voteReviewHelpful(productId, review.id, currentUser.id);
      setReviews(prev => prev.map(r => (r.id === updated.id ? { ...r, helpfulCount: updated.helpfulCount } : r)));
      setHelpfulVotes(prev => new Set(prev).add(`${currentUser.id}:${review.id}`));
    } catch (e) { showToast(errorMessage(e)); }
  }

  async function handleAsk(e: FormEvent) {
    e.preventDefault();
    if (!currentUser || !questionText.trim()) return;
    setQaBusy(true);
    try {
      const created = await askQuestion(productId, { userId: currentUser.id, userName: currentUser.name, text: questionText.trim() });
      setQuestions(prev => [created, ...prev]);
      setQuestionText('');
      showToast(t('qa.asked'));
    } catch (err) { showToast(errorMessage(err)); }
    finally { setQaBusy(false); }
  }

  async function handleAnswer(e: FormEvent, questionId: string) {
    e.preventDefault();
    const text = (answerDrafts[questionId] ?? '').trim();
    if (!currentUser || !text) return;
    setQaBusy(true);
    try {
      const updated = await answerQuestion(productId, questionId, {
        userId: currentUser.id, userName: currentUser.name, text, persona: currentUser.persona,
      });
      setQuestions(prev => prev.map(q => (q.id === updated.id ? updated : q)));
      setAnswerDrafts(prev => ({ ...prev, [questionId]: '' }));
      showToast(t('qa.answered'));
    } catch (err) { showToast(errorMessage(err)); }
    finally { setQaBusy(false); }
  }

  if (!product) return <div data-testid="product-loading">{t('product.loading')}</div>;

  const average = reviews.length
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : 0;
  const maxQuantity = Math.min(MAX_QUANTITY, Math.max(1, product.stock));
  const subscribed = !!currentUser && stockAlert?.userId === currentUser.id && stockAlert.subscribed;

  return (
    <div data-testid="product-page">
      <div className="product-layout">
        <img src={product.imageUrl || 'https://placehold.co/400x300'} alt={product.name} className="product-detail-img" />
        <div className="product-info">
          <h1 data-testid="product-name">{product.name}</h1>
          {(product.reviewCount ?? 0) > 0 && (
            <a href="#reviews-section" className="rating-link" data-testid="product-rating">
              <StarRating rating={product.averageRating ?? 0} count={product.reviewCount} />
            </a>
          )}
          <PriceTag product={product} testId="product-price" large />
          <p data-testid="product-desc">{product.description}</p>
          <div>{t('product.category')}: {product.category}</div>

          <div className="buy-box" data-testid="buy-box">
            <StockNotice stock={product.stock} testId="product-stock" />
            {!outOfStock && (
              <label className="qty-label">
                {t('product.quantity')}
                <select data-testid="product-qty" value={quantity} onChange={e => setQuantity(Number(e.target.value))}>
                  {Array.from({ length: maxQuantity }, (_, i) => i + 1).map(n => <option key={n} value={n}>{formatNumber(n)}</option>)}
                </select>
              </label>
            )}
            <button data-testid="product-add-cart" className="btn-add-cart" onClick={handleAddToCart} disabled={!currentUser || outOfStock}>{t('product.addToCart')}</button>
            <button data-testid="product-buy-now" className="btn-buy-now" onClick={handleBuyNow} disabled={!canShop || outOfStock}>{t('product.buyNow')}</button>
            <button data-testid="product-add-wishlist" className="btn-secondary" onClick={handleAddToWishList} disabled={!canShop || wishListState !== 'idle'}>
              {wishListState === 'added' ? t('product.inWishList') : t('product.addToWishList')}
            </button>
            {wishListState === 'added' && <Link to="/wishlist" data-testid="product-view-wishlist">{t('product.viewWishList')}</Link>}
            {outOfStock && currentUser && (
              <div className="stock-alert" data-testid="stock-alert">
                {subscribed ? (
                  <>
                    <span data-testid="stock-alert-subscribed">{t('stockAlert.subscribed')}</span>
                    <button type="button" className="btn-link" data-testid="stock-alert-cancel" disabled={alertBusy} onClick={() => handleStockAlert(false)}>
                      {t('stockAlert.cancel')}
                    </button>
                  </>
                ) : (
                  <button type="button" className="btn-secondary" data-testid="stock-alert-btn" disabled={alertBusy} onClick={() => handleStockAlert(true)}>
                    {t('stockAlert.notifyMe')}
                  </button>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="product-toast">{toast}</div>}
      </div>
      <RecommendationStrip products={recs} />

      <section data-testid="reviews-section" id="reviews-section">
        <h2>{t('reviews.title')}</h2>
        {reviews.length === 0 ? (
          <div data-testid="reviews-empty">{t('reviews.empty')}</div>
        ) : (
          <>
            <div className="reviews-summary">
              <StarRating rating={average} />
              <div data-testid="reviews-average">
                {t('reviews.average')}: {average.toFixed(1)} / 5 ({t('reviews.count', { count: String(reviews.length) })})
              </div>
              <div className="histogram" data-testid="reviews-histogram" role="group" aria-label={t('reviews.filterByStars')}>
                {STAR_LEVELS.map(stars => {
                  const count = reviews.filter(r => r.rating === stars).length;
                  const percent = Math.round((count / reviews.length) * 100);
                  const active = starFilter === stars;
                  return (
                    <button
                      key={stars}
                      type="button"
                      className={active ? 'histogram-row active' : 'histogram-row'}
                      data-testid={`reviews-filter-${stars}`}
                      aria-pressed={active}
                      disabled={count === 0}
                      onClick={() => setStarFilter(active ? null : stars)}
                    >
                      <span className="histogram-label">{t('reviews.stars', { stars: formatNumber(stars) })}</span>
                      <span className="histogram-bar" aria-hidden="true"><span className="histogram-fill" style={{ width: `${percent}%` }} /></span>
                      <span className="histogram-percent">{formatNumber(percent)}%</span>
                    </button>
                  );
                })}
              </div>
            </div>
            <div className="review-toolbar">
              <label>
                {t('reviews.sortBy')}
                <select data-testid="reviews-sort" value={reviewSort} onChange={e => setReviewSort(e.target.value as ReviewSort)}>
                  <option value="recent">{t('reviews.sort.recent')}</option>
                  <option value="helpful">{t('reviews.sort.helpful')}</option>
                  <option value="highest">{t('reviews.sort.highest')}</option>
                  <option value="lowest">{t('reviews.sort.lowest')}</option>
                </select>
              </label>
              <label className="checkbox-label">
                <input type="checkbox" data-testid="reviews-verified-only" checked={verifiedOnly} onChange={e => setVerifiedOnly(e.target.checked)} />
                {t('reviews.verifiedOnly')}
              </label>
              {starFilter !== null && (
                <button type="button" className="btn-link" data-testid="reviews-filter-clear" onClick={() => setStarFilter(null)}>
                  {t('reviews.clearFilter')}
                </button>
              )}
            </div>
            {visibleReviews.length === 0 && <div data-testid="reviews-filter-empty">{t('reviews.noMatches')}</div>}
            <ul data-testid="reviews-list" className="reviews-list">
              {visibleReviews.map(review => {
                const helpful = review.helpfulCount ?? 0;
                const voted = !!currentUser && helpfulVotes.has(`${currentUser.id}:${review.id}`);
                const canVote = !!currentUser && currentUser.id !== review.userId;
                return (
                  <li key={review.id} data-testid={`review-${review.id}`} className="review">
                    <div className="review-head">
                      <StarRating rating={review.rating} testId={`review-rating-${review.id}`} />
                      <strong>{review.title}</strong>
                    </div>
                    <div className="review-meta">
                      {formatDate(review.createdAt)}
                      {review.verifiedPurchase && <span className="verified-badge" data-testid={`review-verified-${review.id}`}>{t('reviews.verified')}</span>}
                    </div>
                    <p>{review.body}</p>
                    <div className="helpful-row">
                      {helpful > 0 && (
                        <span data-testid={`review-helpful-count-${review.id}`}>
                          {helpful === 1 ? t('reviews.helpfulOne') : t('reviews.helpfulMany', { count: formatNumber(helpful) })}
                        </span>
                      )}
                      {canVote && (voted ? (
                        <span className="helpful-thanks" data-testid={`review-helpful-thanks-${review.id}`}>{t('reviews.helpfulThanks')}</span>
                      ) : (
                        <button type="button" className="btn-secondary btn-small" data-testid={`review-helpful-${review.id}`} onClick={() => handleHelpful(review)}>
                          {t('reviews.helpful')}
                        </button>
                      ))}
                    </div>
                  </li>
                );
              })}
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

      <section className="qa-section" data-testid="qa-section" aria-labelledby="qa-title">
        <h2 id="qa-title">{t('qa.title')}</h2>
        {currentUser ? (
          <form className="question-form" data-testid="question-form" onSubmit={handleAsk}>
            <label htmlFor="question-input">{t('qa.askLabel')}</label>
            <textarea
              id="question-input"
              data-testid="question-input"
              maxLength={500}
              rows={2}
              placeholder={t('qa.askPlaceholder')}
              value={questionText}
              onChange={e => setQuestionText(e.target.value)}
            />
            <button type="submit" data-testid="question-submit" disabled={qaBusy || !questionText.trim()}>{t('qa.ask')}</button>
          </form>
        ) : (
          <div data-testid="qa-no-user">{t('qa.noUser')}</div>
        )}
        {questions.length === 0 && <div data-testid="qa-empty">{t('qa.empty')}</div>}
        <div data-testid="qa-list">
          {questions.map(q => {
            const draft = answerDrafts[q.id] ?? '';
            return (
              <div key={q.id} className="question" data-testid={`question-${q.id}`}>
                <div className="question-text"><strong>{t('qa.question')}</strong> {q.text}</div>
                <div className="qa-meta">{t('qa.askedBy', { name: q.userName })} · {formatDate(q.createdAt)}</div>
                {q.answers.length === 0 && <div className="qa-meta" data-testid={`question-unanswered-${q.id}`}>{t('qa.noAnswers')}</div>}
                {q.answers.map(a => (
                  <div key={a.id} className="answer" data-testid={`answer-${a.id}`}>
                    <strong>{t('qa.answer')}</strong> {a.text}
                    <div className="qa-meta">
                      {t('qa.answeredBy', { name: a.userName })}
                      {a.isSeller && <span className="seller-badge" data-testid={`answer-seller-${a.id}`}>{t('qa.seller')}</span>}
                      {' · '}{formatDate(a.createdAt)}
                    </div>
                  </div>
                ))}
                {currentUser && (
                  <form className="answer-form" onSubmit={e => handleAnswer(e, q.id)}>
                    <input
                      data-testid={`answer-input-${q.id}`}
                      aria-label={t('qa.answerLabel', { question: q.text })}
                      placeholder={t('qa.answerPlaceholder')}
                      maxLength={500}
                      value={draft}
                      onChange={e => setAnswerDrafts(prev => ({ ...prev, [q.id]: e.target.value }))}
                    />
                    <button type="submit" className="btn-secondary btn-small" data-testid={`answer-submit-${q.id}`} disabled={qaBusy || !draft.trim()}>
                      {t('qa.answerSubmit')}
                    </button>
                  </form>
                )}
              </div>
            );
          })}
        </div>
      </section>
    </div>
  );
}
