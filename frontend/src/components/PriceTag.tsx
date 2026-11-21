import { dealPercent, isOnDeal } from '../api/client';
import type { Product } from '../api/client';
import { useLocale } from '../i18n/LocaleContext';

interface Props { product: Pick<Product, 'price' | 'listPrice' | 'currency'>; testId?: string; large?: boolean; }

/** The price, plus the saving and struck-through list price while the product is on deal. */
export default function PriceTag({ product, testId, large }: Props) {
  const { t, formatCurrency, formatNumber } = useLocale();
  const deal = isOnDeal(product);
  return (
    <div className={large ? 'price-tag price-tag-large' : 'price-tag'} data-testid={testId}>
      {deal && <span className="deal-badge">{t('deal.percentOff', { percent: formatNumber(dealPercent(product)) })}</span>}
      <span className="price-now">{formatCurrency(product.price, product.currency)}</span>
      {deal && (
        <span className="price-was">
          {t('deal.listPrice')} <s>{formatCurrency(product.listPrice ?? product.price, product.currency)}</s>
        </span>
      )}
    </div>
  );
}
